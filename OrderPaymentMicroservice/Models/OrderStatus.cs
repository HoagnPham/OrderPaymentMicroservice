namespace OrderPaymentMicroservice.Models;

/// <summary>
/// Durable business lifecycle of an order. Gateway execution states belong to
/// <see cref="PaymentStatus"/>, not to this enum.
/// </summary>
public enum OrderStatus
{
    CREATED = 0,
    VALIDATED = 1,
    PAYMENT_PENDING = 2,
    PAID = 3,
    CONFIRMED = 4,
    COMPLETED = 5,
    CANCELLED = 6,
    FAILED = 7,
    REFUND_PENDING = 8,
    REFUNDED = 9
}
