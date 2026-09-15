namespace OrderPaymentMicroservice.DTOs.Orders;

/// <summary>
/// Order response DTO
/// TODO: Kripke/BMC property - Status should represent valid state in state machine
/// </summary>
public class OrderResponse
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
}
