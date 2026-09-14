using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Users;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace OrderPaymentMicroservice.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserService> _logger;

    public UserService(AppDbContext context, ILogger<UserService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<UserResponse> RegisterAsync(RegisterRequest request)
    {
        if (await EmailExistsAsync(request.Email))
            throw new InvalidOperationException($"Email {request.Email} already registered");

        var user = new User
        {
            Email = request.Email,
            PasswordHash = HashPassword(request.Password),
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User registered: {Email}", request.Email);
        return MapToResponse(user);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null || !VerifyPassword(request.Password, user.PasswordHash))
            throw new InvalidOperationException("Invalid email or password");

        if (!user.IsActive)
            throw new InvalidOperationException("User account is not active");

        // TODO: Implement JWT token generation
        var token = GenerateSimpleToken(user.Id, user.Email);

        _logger.LogInformation("User logged in: {Email}", request.Email);
        return new LoginResponse
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
    }

    public async Task<UserResponse?> GetUserByIdAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        return user != null ? MapToResponse(user) : null;
    }

    public async Task<UserResponse> UpdateUserAsync(int userId, UpdateUserRequest request)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            throw new InvalidOperationException($"User {userId} not found");

        if (!string.IsNullOrEmpty(request.FullName))
            user.FullName = request.FullName;
        if (!string.IsNullOrEmpty(request.PhoneNumber))
            user.PhoneNumber = request.PhoneNumber;
        if (!string.IsNullOrEmpty(request.Address))
            user.Address = request.Address;
        if (!string.IsNullOrEmpty(request.City))
            user.City = request.City;
        if (!string.IsNullOrEmpty(request.PostalCode))
            user.PostalCode = request.PostalCode;

        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("User updated: {UserId}", userId);
        return MapToResponse(user);
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _context.Users.AnyAsync(u => u.Email == email);
    }

    private string HashPassword(string password)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }
    }

    private bool VerifyPassword(string password, string hash)
    {
        var hashOfInput = HashPassword(password);
        return hashOfInput == hash;
    }

    private string GenerateSimpleToken(int userId, string email)
    {
        // TODO: Replace with proper JWT token
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId}:{email}:{DateTime.UtcNow.Ticks}"));
    }

    private UserResponse MapToResponse(User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Address = user.Address,
            City = user.City,
            PostalCode = user.PostalCode,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt
        };
    }
}
