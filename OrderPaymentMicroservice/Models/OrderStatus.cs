namespace OrderPaymentMicroservice.Models;

/// <summary>
/// Order status enumeration
/// TODO: Kripke/BMC property - Order must complete state machine: CREATED -> VALIDATED -> PAYMENT_PENDING -> PAID -> CONFIRMED -> COMPLETED
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
    FAILED = 7
}
