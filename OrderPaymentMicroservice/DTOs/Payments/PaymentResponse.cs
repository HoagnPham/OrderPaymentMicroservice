namespace OrderPaymentMicroservice.DTOs.Payments;

/// <summary>
/// Payment response DTO
/// TODO: SMT verification - Payment.Status must influence Order.Status correctly
/// </summary>
public class PaymentResponse
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? TransactionCode { get; set; }
    public string? RefundTransactionCode { get; set; }
    public int AttemptCount { get; set; }
    public string? LastErrorCode { get; set; }
    public string? GatewayMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
