using OrderPaymentMicroservice.Security;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Users;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Services.Interfaces;
using System.Text;

namespace OrderPaymentMicroservice.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserService> _logger;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IConfiguration _configuration;

    public UserService(
        AppDbContext context,
        ILogger<UserService> logger,
        IPasswordHasher<User> passwordHasher,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
    }

    public async Task<UserResponse> RegisterAsync(RegisterRequest request)
    {
        RequestValidation.Validate(request);
        if (await EmailExistsAsync(request.Email))
            throw new InvalidOperationException($"Email {request.Email} already registered");

        var user = new User
        {
            Email = request.Email,
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User registered: {Email}", request.Email);
        return MapToResponse(user);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        RequestValidation.Validate(request);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null)
            throw new InvalidOperationException("Invalid email or password");

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            if (!VerifyLegacySha256(request.Password, user.PasswordHash))
                throw new InvalidOperationException("Invalid email or password");

            // One-time migration for accounts created before PasswordHasher was adopted.
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
            await _context.SaveChangesAsync();
        }
        else if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
            await _context.SaveChangesAsync();
        }

        if (!user.IsActive)
            throw new InvalidOperationException("User account is not active");

        var expiresAt = DateTime.UtcNow.AddHours(1);
        var token = GenerateJwtToken(user, expiresAt);

        _logger.LogInformation("User logged in: {Email}", request.Email);
        return new LoginResponse
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Token = token,
            ExpiresAt = expiresAt
        };
    }

    public async Task<UserResponse?> GetUserByIdAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        return user != null ? MapToResponse(user) : null;
    }

    public async Task<UserResponse> UpdateUserAsync(int userId, UpdateUserRequest request)
    {
        RequestValidation.Validate(request);
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

    private string GenerateJwtToken(User user, DateTime expiresAt)
    {
        var key = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured");
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static bool VerifyLegacySha256(string password, string storedHash)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != 32)
            return false;

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
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
