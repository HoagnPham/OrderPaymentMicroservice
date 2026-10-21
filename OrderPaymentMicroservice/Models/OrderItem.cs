namespace OrderPaymentMicroservice.Models;

/// <summary>
/// Order item entity - one product line. SubTotal = Quantity * UnitPrice;
/// bounds are enforced by InputValidator and modelled in PaymentVerification.z3.
/// </summary>
public class OrderItem
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to Order
    /// </summary>
    public int OrderId { get; set; }

    /// <summary>
    /// Product ID (from shop/inventory system)
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>
    /// Product quantity; 1..10000 enforced by InputValidator and
    /// CK_OrderItems_Quantity_Positive.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Unit price. The client value is not trusted: OrderService reprices from the
    /// server catalog before persisting.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Subtotal = Quantity * UnitPrice; overflow is checked with checked() in InputValidator.
    /// </summary>
    public decimal SubTotal { get; set; }

    /// <summary>
    /// Navigation property
    /// </summary>
    public virtual Order? Order { get; set; }

    /// <summary>
    /// Navigation property to Product
    /// </summary>
    public virtual Product? Product { get; set; }
}
