using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;

namespace OrderPaymentMicroservice.Utilities;

/// <summary>
/// Database initialization helper
/// Ensures database is created and migrations are applied
/// </summary>
public class DbInitializer
{
    private readonly AppDbContext _context;
    private readonly ILogger<DbInitializer> _logger;

    public DbInitializer(AppDbContext context, ILogger<DbInitializer> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Initialize database - create if not exists and run migrations
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Starting database initialization...");

            // Test connection
            await TestConnectionAsync();

            // Ensure database is created
            await _context.Database.EnsureCreatedAsync();
            _logger.LogInformation("Database ensured to exist");

            // Apply any pending migrations
            var pendingMigrations = await _context.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                _logger.LogInformation("Applying pending migrations...");
                await _context.Database.MigrateAsync();
                _logger.LogInformation("Migrations applied successfully");
            }
            else
            {
                _logger.LogInformation("No pending migrations");
            }

            _logger.LogInformation("Database initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during database initialization");
            throw;
        }
    }

    /// <summary>
    /// Test database connection
    /// </summary>
    private async Task TestConnectionAsync()
    {
        try
        {
            _logger.LogInformation("Testing database connection...");
            var canConnect = await _context.Database.CanConnectAsync();
            if (canConnect)
            {
                _logger.LogInformation("✅ Database connection test SUCCESS");
            }
            else
            {
                _logger.LogError("❌ Database connection test FAILED");
                throw new InvalidOperationException("Cannot connect to database");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Database connection error: {Message}", ex.Message);
            throw;
        }
    }
}
