using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services.Interfaces;
using OrderPaymentMicroservice.Services.Payments;

namespace OrderPaymentMicroservice.Services;

/// <summary>
/// Coordinates the durable payment state machine with an external gateway.
/// Gateway calls deliberately run outside database transactions.
/// </summary>
public sealed class PaymentService : IPaymentService
{
    private const int MaxChargeAttempts = 2;

    private readonly AppDbContext _context;
    private readonly PaymentValidator _validator;
    private readonly IPaymentGateway _gateway;

    public PaymentService(AppDbContext context, PaymentValidator validator, IPaymentGateway gateway)
    {
        _context = context;
        _validator = validator;
        _gateway = gateway;
    }

    public async Task<PaymentResponse> CreatePaymentAsync(CreatePaymentRequest request)
    {
        _validator.ValidateCreatePaymentRequest(request);

        Payment payment;
        await using (var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null)
        {
            var order = await _context.Orders
                .Include(candidate => candidate.Payments)
                .FirstOrDefaultAsync(candidate => candidate.Id == request.OrderId)
                ?? throw new InvalidOperationException($"Order {request.OrderId} not found");

            var previousAttempt = order.Payments.FirstOrDefault(candidate =>
                candidate.IdempotencyKey == request.IdempotencyKey);
            if (previousAttempt != null)
            {
                if (previousAttempt.Amount != request.Amount)
                    throw new InvalidOperationException("Idempotency key was already used with a different amount");
                return MapToResponse(previousAttempt);
            }

            if (order.Payments.Count != 0)
                throw new InvalidOperationException("Order already owns a logical payment; retry that payment instead");
            if (order.Status != OrderStatus.PAYMENT_PENDING)
                throw new InvalidOperationException($"Order in state {order.Status} cannot be paid");
            if (order.InventoryStatus != InventoryReservationStatus.RESERVED)
                throw new InvalidOperationException("Order has no active inventory reservation");
            if (request.Amount != order.TotalAmount)
                throw new InvalidOperationException("Payment amount must equal the order total");

            payment = new Payment
            {
                OrderId = order.Id,
                Amount = request.Amount,
                Status = PaymentStatus.PENDING,
                IdempotencyKey = request.IdempotencyKey,
                AttemptCount = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            PaymentStateMachine.Transition(payment, PaymentStatus.PROCESSING);
            order.Version++;
            order.UpdatedAt = DateTime.UtcNow;
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();
            if (transaction != null)
                await transaction.CommitAsync();
        }

        var result = await ChargeSafelyAsync(payment);
        return await FinalizeChargeAsync(payment.Id, result);
    }

    public async Task<PaymentResponse> RetryPaymentAsync(int paymentId)
    {
        Payment payment;
        var reconcileOnly = false;
        await using (var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null)
        {
            payment = await _context.Payments
                .Include(candidate => candidate.Order)
                .FirstOrDefaultAsync(candidate => candidate.Id == paymentId)
                ?? throw new InvalidOperationException($"Payment {paymentId} not found");

            if (payment.Status != PaymentStatus.UNKNOWN)
                throw new InvalidOperationException($"Payment in state {payment.Status} cannot be retried");
            if (payment.Order?.Status != OrderStatus.PAYMENT_PENDING)
                throw new InvalidOperationException("Payment order is no longer awaiting payment");

            if (payment.AttemptCount >= MaxChargeAttempts)
            {
                payment.LastErrorCode = "RETRY_EXHAUSTED_RECONCILIATION_REQUIRED";
                payment.GatewayMessage = "Automatic charge retry budget exhausted; final outcome must be reconciled";
                payment.UpdatedAt = DateTime.UtcNow;
                payment.Version++;
                await _context.SaveChangesAsync();
                if (transaction != null)
                    await transaction.CommitAsync();
                reconcileOnly = true;
            }
            else
            {
                PaymentStateMachine.Transition(payment, PaymentStatus.PROCESSING);
                payment.AttemptCount++;
                payment.LastErrorCode = null;
                payment.GatewayMessage = null;
                await _context.SaveChangesAsync();
                if (transaction != null)
                    await transaction.CommitAsync();
            }
        }

        if (reconcileOnly)
            return await ReconcilePaymentAsync(payment.Id);

        var result = await ChargeSafelyAsync(payment);
        return await FinalizeChargeAsync(payment.Id, result);
    }

    public async Task<PaymentResponse> ReconcilePaymentAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAggregateAsync(paymentId, cancellationToken)
            ?? throw new InvalidOperationException($"Payment {paymentId} not found");
        if (payment.Status is not (PaymentStatus.PROCESSING or PaymentStatus.UNKNOWN))
            return MapToResponse(payment);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await GetChargeStatusSafelyAsync(payment, cancellationToken);
        if (payment.Status == PaymentStatus.UNKNOWN && result.Outcome != GatewayOutcome.Unknown)
        {
            PaymentStateMachine.Transition(payment, PaymentStatus.PROCESSING);
            await _context.SaveChangesAsync(cancellationToken);
        }
        else if (payment.Status == PaymentStatus.UNKNOWN)
        {
            payment.LastErrorCode = payment.AttemptCount >= MaxChargeAttempts
                ? "RETRY_EXHAUSTED_RECONCILIATION_REQUIRED"
                : result.ErrorCode ?? "OUTCOME_STILL_UNKNOWN";
            payment.GatewayMessage = result.Message;
            payment.UpdatedAt = DateTime.UtcNow;
            payment.Version++;
            await _context.SaveChangesAsync(cancellationToken);
            return MapToResponse(payment);
        }

        return await FinalizeChargeAsync(payment.Id, result, cancellationToken);
    }

    public async Task<PaymentResponse> RefundPaymentAsync(int paymentId, string refundIdempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(refundIdempotencyKey) || refundIdempotencyKey.Length > 128)
            throw new ArgumentException("A refund idempotency key of at most 128 characters is required", nameof(refundIdempotencyKey));

        Payment payment;
        await using (var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null)
        {
            payment = await LoadPaymentAggregateAsync(paymentId)
                ?? throw new InvalidOperationException($"Payment {paymentId} not found");

            if (payment.Status == PaymentStatus.REFUNDED)
            {
                if (payment.RefundIdempotencyKey != refundIdempotencyKey)
                    throw new InvalidOperationException("Payment was already refunded with another idempotency key");
                return MapToResponse(payment);
            }
            if (payment.Status == PaymentStatus.REFUND_PENDING)
            {
                if (payment.RefundIdempotencyKey != refundIdempotencyKey)
                    throw new InvalidOperationException("A refund is already pending with another idempotency key");
                return MapToResponse(payment);
            }
            if (payment.Status != PaymentStatus.SUCCESS)
                throw new InvalidOperationException($"Payment in state {payment.Status} cannot be refunded");
            if (payment.Order == null || !OrderStateMachine.CanTransition(payment.Order.Status, OrderStatus.REFUND_PENDING))
                throw new InvalidOperationException($"Order in state {payment.Order?.Status} cannot be refunded");

            payment.RefundIdempotencyKey = refundIdempotencyKey.Trim();
            PaymentStateMachine.Transition(payment, PaymentStatus.REFUND_PENDING);
            OrderStateMachine.Transition(payment.Order, OrderStatus.REFUND_PENDING);
            await _context.SaveChangesAsync();
            if (transaction != null)
                await transaction.CommitAsync();
        }

        var result = await RefundSafelyAsync(payment);
        return await FinalizeRefundAsync(payment.Id, result);
    }

    public async Task<PaymentResponse> RetryRefundAsync(int paymentId)
    {
        var payment = await LoadPaymentAggregateAsync(paymentId)
            ?? throw new InvalidOperationException($"Payment {paymentId} not found");
        if (payment.Status != PaymentStatus.REFUND_PENDING || payment.Order?.Status != OrderStatus.REFUND_PENDING)
            throw new InvalidOperationException("Only a pending refund can be retried");

        var result = await RefundSafelyAsync(payment);
        return await FinalizeRefundAsync(payment.Id, result);
    }

    public async Task<PaymentResponse> ReconcileRefundAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAggregateAsync(paymentId, cancellationToken)
            ?? throw new InvalidOperationException($"Payment {paymentId} not found");
        if (payment.Status != PaymentStatus.REFUND_PENDING)
            return MapToResponse(payment);
        if (payment.Order?.Status != OrderStatus.REFUND_PENDING)
            throw new InvalidOperationException("Pending refund has an inconsistent order state");

        cancellationToken.ThrowIfCancellationRequested();
        var result = await GetRefundStatusSafelyAsync(payment, cancellationToken);
        return await FinalizeRefundAsync(payment.Id, result, cancellationToken);
    }

    public async Task<PaymentResponse?> GetPaymentByIdAsync(int paymentId)
    {
        var payment = await _context.Payments.FindAsync(paymentId);
        return payment == null ? null : MapToResponse(payment);
    }

    private async Task<GatewayResult> ChargeSafelyAsync(Payment payment)
    {
        try
        {
            return await _gateway.ChargeAsync(new GatewayPaymentRequest(
                payment.Id, payment.OrderId, payment.Amount, payment.IdempotencyKey!, payment.TransactionCode));
        }
        catch (TimeoutException ex)
        {
            return new GatewayResult(GatewayOutcome.Unknown, ErrorCode: "GATEWAY_TIMEOUT", Message: ex.Message);
        }
        catch (Exception ex)
        {
            return new GatewayResult(GatewayOutcome.Unknown, payment.TransactionCode, "GATEWAY_EXCEPTION", ex.Message);
        }
    }

    private async Task<GatewayResult> RefundSafelyAsync(Payment payment)
    {
        try
        {
            return await _gateway.RefundAsync(new GatewayPaymentRequest(
                payment.Id, payment.OrderId, payment.Amount, payment.RefundIdempotencyKey!, payment.TransactionCode));
        }
        catch (TimeoutException ex)
        {
            return new GatewayResult(GatewayOutcome.Unknown, ErrorCode: "REFUND_GATEWAY_TIMEOUT", Message: ex.Message);
        }
        catch (Exception ex)
        {
            return new GatewayResult(GatewayOutcome.Unknown, payment.TransactionCode, "REFUND_GATEWAY_EXCEPTION", ex.Message);
        }
    }

    private async Task<GatewayResult> GetChargeStatusSafelyAsync(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.GetChargeStatusAsync(new GatewayPaymentRequest(
                payment.Id, payment.OrderId, payment.Amount, payment.IdempotencyKey!, payment.TransactionCode), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TimeoutException ex)
        {
            return new GatewayResult(GatewayOutcome.Unknown, ErrorCode: "RECONCILIATION_TIMEOUT", Message: ex.Message);
        }
        catch (Exception ex)
        {
            return new GatewayResult(GatewayOutcome.Unknown, payment.TransactionCode, "RECONCILIATION_EXCEPTION", ex.Message);
        }
    }

    private async Task<GatewayResult> GetRefundStatusSafelyAsync(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.GetRefundStatusAsync(new GatewayPaymentRequest(
                payment.Id, payment.OrderId, payment.Amount, payment.RefundIdempotencyKey!, payment.TransactionCode), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TimeoutException ex)
        {
            return new GatewayResult(GatewayOutcome.Unknown, ErrorCode: "REFUND_RECONCILIATION_TIMEOUT", Message: ex.Message);
        }
        catch (Exception ex)
        {
            return new GatewayResult(GatewayOutcome.Unknown, payment.RefundTransactionCode, "REFUND_RECONCILIATION_EXCEPTION", ex.Message);
        }
    }

    private async Task<PaymentResponse> FinalizeChargeAsync(int paymentId, GatewayResult result, CancellationToken cancellationToken = default)
    {
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        var payment = await LoadPaymentAggregateAsync(paymentId, cancellationToken)
            ?? throw new InvalidOperationException($"Payment {paymentId} disappeared during processing");

        if (payment.Status != PaymentStatus.PROCESSING)
            return MapToResponse(payment);

        payment.TransactionCode = result.TransactionCode ?? payment.TransactionCode;
        payment.LastErrorCode = result.ErrorCode;
        payment.GatewayMessage = result.Message;

        switch (result.Outcome)
        {
            case GatewayOutcome.Succeeded:
                if (payment.Order?.Status != OrderStatus.PAYMENT_PENDING)
                    throw new InvalidOperationException("Successful gateway result cannot be applied to the current order state");
                PaymentStateMachine.Transition(payment, PaymentStatus.SUCCESS);
                OrderStateMachine.Transition(payment.Order, OrderStatus.PAID);
                break;
            case GatewayOutcome.Declined:
                PaymentStateMachine.Transition(payment, PaymentStatus.FAILED);
                await FailOrderAndReleaseInventoryAsync(payment.Order
                    ?? throw new InvalidOperationException("Payment has no order"));
                break;
            case GatewayOutcome.Unknown:
                PaymentStateMachine.Transition(payment, PaymentStatus.UNKNOWN);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result));
        }

        await _context.SaveChangesAsync(cancellationToken);
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);
        return MapToResponse(payment);
    }

    private async Task<PaymentResponse> FinalizeRefundAsync(int paymentId, GatewayResult result, CancellationToken cancellationToken = default)
    {
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        var payment = await LoadPaymentAggregateAsync(paymentId, cancellationToken)
            ?? throw new InvalidOperationException($"Payment {paymentId} disappeared during refund");

        if (payment.Status != PaymentStatus.REFUND_PENDING)
            return MapToResponse(payment);

        payment.LastErrorCode = result.ErrorCode;
        payment.GatewayMessage = result.Message;
        if (result.Outcome == GatewayOutcome.Succeeded)
        {
            payment.RefundTransactionCode = result.TransactionCode;
            PaymentStateMachine.Transition(payment, PaymentStatus.REFUNDED);
            if (payment.Order?.Status != OrderStatus.REFUND_PENDING)
                throw new InvalidOperationException("Refunded payment has an inconsistent order state");
            OrderStateMachine.Transition(payment.Order, OrderStatus.REFUNDED);
            await InventoryReservation.ReleaseAsync(_context, payment.Order);
        }
        else
        {
            payment.LastErrorCode ??= result.Outcome == GatewayOutcome.Declined
                ? "REFUND_REJECTED"
                : "REFUND_OUTCOME_UNKNOWN";
            payment.UpdatedAt = DateTime.UtcNow;
            payment.Version++;
        }

        await _context.SaveChangesAsync(cancellationToken);
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);
        return MapToResponse(payment);
    }

    private async Task FailOrderAndReleaseInventoryAsync(Order order)
    {
        if (order.Status != OrderStatus.PAYMENT_PENDING)
            throw new InvalidOperationException($"Order in state {order.Status} cannot fail payment");
        OrderStateMachine.Transition(order, OrderStatus.FAILED);
        await InventoryReservation.ReleaseAsync(_context, order);
    }

    private Task<Payment?> LoadPaymentAggregateAsync(int paymentId, CancellationToken cancellationToken = default) => _context.Payments
        .Include(payment => payment.Order)!
            .ThenInclude(order => order!.OrderItems)
        .FirstOrDefaultAsync(payment => payment.Id == paymentId, cancellationToken);

    private static PaymentResponse MapToResponse(Payment payment) => new()
    {
        Id = payment.Id,
        OrderId = payment.OrderId,
        Amount = payment.Amount,
        Status = payment.Status.ToString(),
        TransactionCode = payment.TransactionCode,
        RefundTransactionCode = payment.RefundTransactionCode,
        AttemptCount = payment.AttemptCount,
        LastErrorCode = payment.LastErrorCode,
        GatewayMessage = payment.GatewayMessage,
        CreatedAt = payment.CreatedAt,
        UpdatedAt = payment.UpdatedAt
    };
}
