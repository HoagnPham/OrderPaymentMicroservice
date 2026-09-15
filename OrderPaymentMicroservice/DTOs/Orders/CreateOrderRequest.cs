namespace OrderPaymentMicroservice.DTOs.Orders;

/// <summary>
/// Create order request DTO
/// TODO: Fuzzing target - Input validation for total amount and items
/// </summary>
public class CreateOrderRequest
{
    public int CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}
