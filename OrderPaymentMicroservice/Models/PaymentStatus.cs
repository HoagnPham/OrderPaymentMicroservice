namespace OrderPaymentMicroservice.Models;

/// <summary>
/// Durable lifecycle of one logical payment. Numeric values 0-3 are preserved
/// for compatibility with existing databases.
/// </summary>
public enum PaymentStatus
{
    PENDING = 0,
    SUCCESS = 1,
    FAILED = 2,
    REFUNDED = 3,
    PROCESSING = 4,
    UNKNOWN = 5,
    REFUND_PENDING = 6
}
