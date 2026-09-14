using OrderPaymentMicroservice.DTOs.Users;

namespace OrderPaymentMicroservice.Services.Interfaces;

/// <summary>
/// User service interface
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Đăng ký user mới
    /// </summary>
    Task<UserResponse> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Đăng nhập
    /// </summary>
    Task<LoginResponse> LoginAsync(LoginRequest request);

    /// <summary>
    /// Lấy user theo ID
    /// </summary>
    Task<UserResponse?> GetUserByIdAsync(int userId);

    /// <summary>
    /// Cập nhật profile user
    /// </summary>
    Task<UserResponse> UpdateUserAsync(int userId, UpdateUserRequest request);

    /// <summary>
    /// Kiểm tra email đã tồn tại chưa
    /// </summary>
    Task<bool> EmailExistsAsync(string email);
}
