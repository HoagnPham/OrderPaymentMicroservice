namespace OrderPaymentMicroservice.Models;

/// <summary>
/// Order item entity - represents a product line in an order
/// TODO: SMT verification - SubTotal = Quantity * UnitPrice
/// TODO: Fuzzing target - Quantity and Prices must be positive
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
    /// Product quantity
    /// TODO: Fuzzing input - must be > 0
    /// TODO: Security test - validate quantity <= reasonable limit
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Unit price (in cents or base currency unit)
    /// TODO: Fuzzing input - must be >= 0
    /// TODO: Security test - detect price manipulation
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Subtotal = Quantity * UnitPrice
    /// TODO: SMT verification - SubTotal >= 0
    /// </summary>
    public decimal SubTotal { get; set; }

    /// <summary>
    /// Navigation property
    /// </summary>
    public virtual Order? Order { get; set; }
}
