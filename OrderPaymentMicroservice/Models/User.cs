namespace OrderPaymentMicroservice.Models;

/// <summary>
/// User entity - đại diện cho khách hàng.
/// Password hashing dùng ASP.NET Core PasswordHasher trong UserService;
/// JWT được phát hành trong UserService.GenerateJwtToken.
/// </summary>
public class User
{
    public int Id { get; set; }

    /// <summary>
    /// Email đăng nhập; định dạng do [EmailAddress] trên UserRequest kiểm tra.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Mật khẩu đã hash bằng ASP.NET Core PasswordHasher (PBKDF2, có version).
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Tên khách hàng
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Số điện thoại
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Địa chỉ
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// City/Thành phố
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Postal code
    /// </summary>
    public string? PostalCode { get; set; }

    /// <summary>
    /// Role của user (Admin, Customer)
    /// </summary>
    public string Role { get; set; } = "Customer";

    /// <summary>
    /// Trạng thái tài khoản
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Ngày tạo tài khoản
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ngày cập nhật
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Tập hợp các đơn hàng của user (1-N relationship)
    /// </summary>
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
