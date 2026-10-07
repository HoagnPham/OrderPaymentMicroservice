namespace OrderPaymentMicroservice.DTOs.Orders;

/// <summary>Order state and immutable item snapshot returned by the API.</summary>
public class OrderResponse
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string InventoryStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
}
