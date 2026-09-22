namespace OrderPaymentMicroservice.Models;

/// <summary>
/// Payment entity
/// TODO: SMT verification - Payment.Amount must equal Order.TotalAmount or be partial
/// TODO: Kripke/BMC property - Order transitions to PAID only when Payment.Status == SUCCESS
/// </summary>
public class Payment
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to Order
    /// </summary>
    public int OrderId { get; set; }

    /// <summary>
    /// Payment amount (in cents or base currency unit)
    /// TODO: SMT verification - Amount > 0
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Current payment status
    /// TODO: Kripke/BMC property - FAILED status must prevent order completion
    /// </summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.PENDING;

    /// <summary>
    /// External transaction code (from payment gateway)
    /// </summary>
    public string? TransactionCode { get; set; }

    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Payment creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property
    /// </summary>
    public virtual Order? Order { get; set; }
}
