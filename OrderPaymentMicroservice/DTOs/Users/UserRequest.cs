using System.ComponentModel.DataAnnotations;

namespace OrderPaymentMicroservice.DTOs.Users;

public class RegisterRequest
{
    [Required, EmailAddress, StringLength(255)]
    public string Email { get; set; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
    [Required, StringLength(255)]
    public string FullName { get; set; } = string.Empty;
    [StringLength(20)]
    public string? PhoneNumber { get; set; }
}

public class LoginRequest
{
    [Required, EmailAddress, StringLength(255)]
    public string Email { get; set; } = string.Empty;
    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;
}

public class UpdateUserRequest
{
    [StringLength(255)]
    public string? FullName { get; set; }
    [StringLength(20)]
    public string? PhoneNumber { get; set; }
    [StringLength(500)]
    public string? Address { get; set; }
    [StringLength(100)]
    public string? City { get; set; }
    [StringLength(20)]
    public string? PostalCode { get; set; }
}

public class UserResponse
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LoginResponse
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
