namespace OrderPaymentMicroservice.Models;

/// <summary>
/// Payment status enumeration
/// TODO: SMT verification - PaymentFailed cannot lead to COMPLETED order
/// </summary>
public enum PaymentStatus
{
    PENDING = 0,
    SUCCESS = 1,
    FAILED = 2,
    REFUNDED = 3
}
