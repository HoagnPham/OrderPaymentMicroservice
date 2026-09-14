namespace OrderPaymentMicroservice.Models;

/// <summary>
/// ShoppingCart entity - giỏ hàng của khách hàng
/// </summary>
public class ShoppingCart
{
    public int Id { get; set; }

    /// <summary>
    /// User ID (khách hàng sở hữu giỏ)
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Tổng tiền trong giỏ
    /// TODO: SMT verification - TotalPrice = sum of items subtotal
    /// </summary>
    public decimal TotalPrice { get; set; }

    /// <summary>
    /// Ngày tạo giỏ hàng
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ngày cập nhật giỏ hàng
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Các item trong giỏ hàng
    /// </summary>
    public virtual ICollection<CartItem> Items { get; set; } = new List<CartItem>();

    /// <summary>
    /// Navigation property
    /// </summary>
    public virtual User? User { get; set; }
}

/// <summary>
/// CartItem entity - một item trong giỏ hàng
/// </summary>
public class CartItem
{
    public int Id { get; set; }

    /// <summary>
    /// Shopping Cart ID
    /// </summary>
    public int CartId { get; set; }

    /// <summary>
    /// Product ID
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>
    /// Số lượng sản phẩm trong giỏ
    /// TODO: Fuzzing - validate quantity > 0
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Giá tại thời điểm thêm vào giỏ
    /// (có thể khác với giá hiện tại)
    /// </summary>
    public decimal PriceAtAdd { get; set; }

    /// <summary>
    /// Subtotal = Quantity * PriceAtAdd
    /// </summary>
    public decimal SubTotal { get; set; }

    /// <summary>
    /// Navigation properties
    /// </summary>
    public virtual ShoppingCart? Cart { get; set; }
    public virtual Product? Product { get; set; }
}
