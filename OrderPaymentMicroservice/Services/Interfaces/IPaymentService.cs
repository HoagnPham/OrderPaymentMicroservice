using OrderPaymentMicroservice.DTOs.Payments;

namespace OrderPaymentMicroservice.Services.Interfaces;

/// <summary>
/// Payment service interface
/// TODO: Kripke/BMC - define payment and order status transition contracts
/// TODO: SMT - verify payment amount constraints
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Create a payment for an order
    /// TODO: Fuzzing target - validate payment amount
    /// TODO: SMT verification - Amount must match or be less than Order.TotalAmount
    /// </summary>
    Task<PaymentResponse> CreatePaymentAsync(CreatePaymentRequest request);

    /// <summary>
    /// Get payment by ID
    /// TODO: Security test - check payment exists and access control
    /// </summary>
    Task<PaymentResponse?> GetPaymentByIdAsync(int paymentId);
}
