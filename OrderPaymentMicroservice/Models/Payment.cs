namespace OrderPaymentMicroservice.Models;

/// <summary>One logical, idempotent payment attempt for an order.</summary>
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
    /// Current durable gateway status; mutations go through PaymentStateMachine.
    /// </summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.PENDING;

    /// <summary>
    /// External transaction code (from payment gateway)
    /// </summary>
    public string? TransactionCode { get; set; }

    public string? RefundTransactionCode { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? RefundIdempotencyKey { get; set; }

    public int AttemptCount { get; set; }

    public string? LastErrorCode { get; set; }

    public string? GatewayMessage { get; set; }

    /// <summary>
    /// Payment creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PendingSince { get; set; }

    public long Version { get; set; }

    /// <summary>
    /// Navigation property
    /// </summary>
    public virtual Order? Order { get; set; }
}
