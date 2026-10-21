namespace OrderPaymentMicroservice.DTOs.Payments;

/// <summary>
/// Create payment request DTO
/// Amount bounds and integer cents are checked by PaymentValidator; the fuzz campaign covers them.
/// </summary>
public class CreatePaymentRequest
{
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
