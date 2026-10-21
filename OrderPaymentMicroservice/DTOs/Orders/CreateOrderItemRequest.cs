namespace OrderPaymentMicroservice.DTOs.Orders;

/// <summary>
/// Order item request in CreateOrderRequest
/// Quantity and price bounds are validated by InputValidator and covered by fuzzing.
/// </summary>
public class CreateOrderItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
