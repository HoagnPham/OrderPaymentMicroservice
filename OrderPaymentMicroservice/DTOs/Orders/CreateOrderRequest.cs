namespace OrderPaymentMicroservice.DTOs.Orders;

/// <summary>
/// Create order request DTO
/// TODO: Fuzzing target - Input validation for total amount and items
/// </summary>
public class CreateOrderRequest
{
    public int CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}
