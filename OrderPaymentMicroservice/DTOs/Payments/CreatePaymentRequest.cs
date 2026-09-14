namespace OrderPaymentMicroservice.DTOs.Payments;

/// <summary>
/// Create payment request DTO
/// TODO: Fuzzing input - validate amount
/// </summary>
public class CreatePaymentRequest
{
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
}
