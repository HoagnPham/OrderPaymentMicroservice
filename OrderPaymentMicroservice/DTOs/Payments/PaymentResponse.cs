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
    public DateTime CreatedAt { get; set; }
}
