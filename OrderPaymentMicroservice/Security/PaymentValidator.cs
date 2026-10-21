using OrderPaymentMicroservice.DTOs.Payments;

namespace OrderPaymentMicroservice.Security;

/// <summary>
/// Payment validation: amount bounds, integer cents and idempotency key shape.
/// SMT counterpart: Security/PaymentVerification.z3, checked by verify_payment_smt.py.
/// Fuzz counterpart: SecurityFuzzingTests payment-amount campaign.
/// </summary>
public class PaymentValidator
{
    // Validation constants
    private const decimal MIN_PAYMENT_AMOUNT = 0.01m;
    private const decimal MAX_PAYMENT_AMOUNT = 999999.99m;

    /// <summary>
    /// Validate create payment request.
    /// Bounds are mirrored by PaymentVerification.z3 TEST4/TEST5 and by the
    /// payment-amount fuzz campaign.
    /// </summary>
    public void ValidateCreatePaymentRequest(CreatePaymentRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request), "Payment request cannot be null");

        // Validate order ID
        if (request.OrderId <= 0)
            throw new ArgumentException("OrderId must be greater than 0", nameof(request.OrderId));

        // Validate payment amount
        if (request.Amount <= 0)
            throw new ArgumentException("Amount must be greater than 0", nameof(request.Amount));

        if (request.Amount < MIN_PAYMENT_AMOUNT)
            throw new ArgumentException($"Amount must be at least {MIN_PAYMENT_AMOUNT}", nameof(request.Amount));

        if (request.Amount > MAX_PAYMENT_AMOUNT)
            throw new ArgumentException($"Amount cannot exceed {MAX_PAYMENT_AMOUNT}", nameof(request.Amount));

        MoneyAmounts.RequireCents(request.Amount, nameof(request.Amount));

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("IdempotencyKey is required", nameof(request.IdempotencyKey));

        if (request.IdempotencyKey.Length > 128)
            throw new ArgumentException("IdempotencyKey cannot exceed 128 characters", nameof(request.IdempotencyKey));

        // The validator has no order context, so amount-versus-order-total is
        // enforced in PaymentService; PaymentVerification.z3 TEST1/TEST3 model both sides.
    }
}
