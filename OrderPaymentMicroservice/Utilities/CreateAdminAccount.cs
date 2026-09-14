using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.Models;
using System.Security.Cryptography;
using System.Text;

namespace OrderPaymentMicroservice.Utilities;

/// <summary>
/// Utility class to create admin account for testing
/// </summary>
public class CreateAdminAccount
{
    private readonly AppDbContext _context;
    private readonly ILogger<CreateAdminAccount> _logger;

    public CreateAdminAccount(AppDbContext context, ILogger<CreateAdminAccount> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Create admin account if not already exists
    /// Default credentials: admin@example.com / Admin@123456
    /// </summary>
    public async Task CreateDefaultAdminAsync()
    {
        try
        {
            const string adminEmail = "admin@example.com";
            const string adminPassword = "Admin@123456";

            // Check if admin already exists
            var existingAdmin = await _context.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
            if (existingAdmin != null)
            {
                _logger.LogInformation("Admin account already exists: {Email}", adminEmail);
                return;
            }

            var adminUser = new User
            {
                Email = adminEmail,
                FullName = "Administrator",
                PasswordHash = HashPassword(adminPassword),
                PhoneNumber = "+84938000000",
                Address = "Ha Noi",
                City = "Ha Noi",
                PostalCode = "100000",
                Role = "Admin", // Set role to Admin
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync();

            _logger.LogInformation("✅ Admin account created successfully!");
            _logger.LogInformation("Email: {Email}", adminEmail);
            _logger.LogInformation("Password: {Password}", adminPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating admin account: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Hash password using SHA256
    /// </summary>
    private static string HashPassword(string password)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }
    }
}
