namespace OrderPaymentMicroservice.DTOs.Orders;

/// <summary>
/// Order item request in CreateOrderRequest
/// TODO: Fuzzing input - validate quantity and prices
/// </summary>
public class CreateOrderItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
