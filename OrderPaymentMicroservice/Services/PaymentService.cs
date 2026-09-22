using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services.Interfaces;

namespace OrderPaymentMicroservice.Services;

/// <summary>
/// Payment service implementation - skeleton version
/// TODO: Integrate with actual payment gateway
/// TODO: SMT verification - payment amount constraints
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly AppDbContext _context;
    private readonly PaymentValidator _validator;

    public PaymentService(AppDbContext context, PaymentValidator validator)
    {
        _context = context;
        _validator = validator;
    }

    /// <summary>
    /// Create a payment for an order
    /// TODO: Fuzzing target - test payment amount validation
    /// TODO: Integrate with payment gateway API
    /// </summary>
    public async Task<PaymentResponse> CreatePaymentAsync(CreatePaymentRequest request)
    {
        _validator.ValidateCreatePaymentRequest(request);

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;

        var order = await _context.Orders
            .Include(candidate => candidate.Payments)
            .FirstOrDefaultAsync(candidate => candidate.Id == request.OrderId);
        if (order == null)
            throw new InvalidOperationException($"Order {request.OrderId} not found");

        var previousAttempt = order.Payments.FirstOrDefault(payment =>
            payment.IdempotencyKey == request.IdempotencyKey);
        if (previousAttempt != null)
        {
            if (previousAttempt.Amount != request.Amount)
                throw new InvalidOperationException("Idempotency key was already used with a different amount");

            return MapToResponse(previousAttempt);
        }

        if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.PAID))
            throw new InvalidOperationException($"Order in state {order.Status} cannot be paid");

        if (order.Payments.Any(payment => payment.Status == PaymentStatus.SUCCESS))
            throw new InvalidOperationException("Order already has a successful payment");

        if (request.Amount != order.TotalAmount)
            throw new InvalidOperationException("Payment amount must equal the order total");

        // TODO: Call payment gateway to process payment
        // For skeleton, we simulate success
        var payment = new Payment
        {
            OrderId = request.OrderId,
            Amount = request.Amount,
            Status = PaymentStatus.SUCCESS, // Skeleton - simulated
            TransactionCode = $"TXN-{Guid.NewGuid().ToString().Substring(0, 8)}",
            IdempotencyKey = request.IdempotencyKey,
            CreatedAt = DateTime.UtcNow
        };

        // Update order status when payment succeeds
        if (payment.Status == PaymentStatus.SUCCESS)
        {
            OrderStateMachine.Transition(order, OrderStatus.PAID);
        }

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();
        if (transaction != null)
            await transaction.CommitAsync();

        return MapToResponse(payment);
    }

    /// <summary>
    /// Get payment by ID
    /// TODO: Security test - check payment exists and access control
    /// </summary>
    public async Task<PaymentResponse?> GetPaymentByIdAsync(int paymentId)
    {
        var payment = await _context.Payments.FindAsync(paymentId);
        return payment != null ? MapToResponse(payment) : null;
    }

    private PaymentResponse MapToResponse(Payment payment)
    {
        return new PaymentResponse
        {
            Id = payment.Id,
            OrderId = payment.OrderId,
            Amount = payment.Amount,
            Status = payment.Status.ToString(),
            TransactionCode = payment.TransactionCode,
            CreatedAt = payment.CreatedAt
        };
    }
}
