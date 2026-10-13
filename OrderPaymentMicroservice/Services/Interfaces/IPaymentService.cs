using OrderPaymentMicroservice.DTOs.Payments;

namespace OrderPaymentMicroservice.Services.Interfaces;

/// <summary>Payment lifecycle, recovery and compensation contract.</summary>
public interface IPaymentService
{
    /// <summary>
    /// Create a payment for an order
    /// Amount must exactly match Order.TotalAmount.
    /// </summary>
    Task<PaymentResponse> CreatePaymentAsync(CreatePaymentRequest request);

    /// <summary>
    /// Get payment by ID
    /// TODO: Security test - check payment exists and access control
    /// </summary>
    Task<PaymentResponse?> GetPaymentByIdAsync(int paymentId);

    Task<PaymentResponse> RetryPaymentAsync(int paymentId);

    Task<PaymentResponse> ReconcilePaymentAsync(int paymentId, CancellationToken cancellationToken = default);

    Task<PaymentResponse> RefundPaymentAsync(int paymentId, string refundIdempotencyKey);

    Task<PaymentResponse> RetryRefundAsync(int paymentId);

    Task<PaymentResponse> ReconcileRefundAsync(int paymentId, CancellationToken cancellationToken = default);
}
