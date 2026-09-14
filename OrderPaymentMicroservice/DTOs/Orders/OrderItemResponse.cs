namespace OrderPaymentMicroservice.DTOs.Orders;

/// <summary>
/// Order item response in OrderResponse
/// </summary>
public class OrderItemResponse
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SubTotal { get; set; }
}
