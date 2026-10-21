namespace OrderPaymentMicroservice.Models;

/// <summary>
/// Product entity - đại diện cho sản phẩm trong shop
/// </summary>
public class Product
{
    public int Id { get; set; }

    /// <summary>
    /// Tên sản phẩm
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả sản phẩm
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Giá sản phẩm; CK_Products_Price_Positive bảo đảm > 0.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Số lượng tồn kho; CK_Products_Stock_Nonnegative bảo đảm >= 0.
    /// </summary>
    public int Stock { get; set; }

    /// <summary>
    /// URL hình ảnh sản phẩm
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Danh mục sản phẩm (e.g., "Món chính", "Tráng miệng")
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Ngày tạo sản phẩm
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ngày cập nhật
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Trạng thái sản phẩm (active/inactive)
    /// </summary>
    public bool IsActive { get; set; } = true;
}
