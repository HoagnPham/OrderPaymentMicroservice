using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Models;
using System.Security.Claims;

namespace OrderPaymentMicroservice.Data;

/// <summary>Entity Framework Core persistence model and database invariants.</summary>
public class AppDbContext : DbContext
{
    private readonly IHttpContextAccessor? _http;
    public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor? http = null) : base(options)
    {
        _http = http;
    }

    // PostgreSQL row triggers write COMMIT audit in the same transaction as the mutation.
    // Transaction-local metadata cannot bleed into a pooled connection's next request.
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var changes = ChangeTracker.Entries().Any(e => (e.Entity is Order or Payment) &&
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        if (!changes || Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        await using var owned = Database.CurrentTransaction == null
            ? await Database.BeginTransactionAsync(cancellationToken) : null;
        var http = _http?.HttpContext;
        var correlation = http?.Items["AuditCorrelationId"] is Guid id ? id : Guid.NewGuid();
        var claim = http?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actor = int.TryParse(claim, out var userId) && userId > 0 ? userId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        var kind = http == null ? "SYSTEM" : actor.Length > 0 ? "USER" : "ANONYMOUS";
        await Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.audit_correlation', {correlation.ToString()}, true), set_config('app.audit_actor', {actor}, true), set_config('app.audit_kind', {kind}, true)", cancellationToken);
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (owned != null) await owned.CommitAsync(cancellationToken);
        return saved;
    }

    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
    public DbSet<Payment> Payments { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<ShoppingCart> ShoppingCarts { get; set; } = null!;
    public DbSet<CartItem> CartItems { get; set; } = null!;

    public DbSet<AuditEvent> AuditEvents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActorKind).HasMaxLength(16).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ResourceType).HasMaxLength(16).IsRequired();
            entity.Property(e => e.Phase).HasMaxLength(16).IsRequired();
            entity.Property(e => e.Outcome).HasMaxLength(32).IsRequired();
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => e.CreatedAt);
        });

        // Configure Order entity
        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(o => o.CreationKeyHash).HasMaxLength(64);
            entity.Property(o => o.CreationRequestHash).HasMaxLength(64);
            entity.Property(o => o.CreationResponseJson).HasColumnType("jsonb");
            entity.HasIndex(o => new { o.CustomerId, o.CreationKeyHash }).IsUnique();
            entity.HasKey(o => o.Id);
            entity.Property(o => o.CustomerId).IsRequired();
            entity.Property(o => o.TotalAmount).HasPrecision(19, 2).IsRequired();
            entity.Property(o => o.Status).HasConversion<int>().IsRequired();
            entity.Property(o => o.InventoryStatus).HasConversion<int>().IsRequired();
            entity.Property(o => o.CreatedAt).IsRequired();
            entity.Property(o => o.UpdatedAt).IsRequired();
            entity.Property(o => o.Version).IsConcurrencyToken().IsRequired();

            // One-to-many: Order -> OrderItems
            entity.HasMany(o => o.OrderItems)
                .WithOne(oi => oi.Order)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-many: Order -> Payments
            entity.HasMany(o => o.Payments)
                .WithOne(p => p.Order)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure OrderItem entity
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems", table =>
            {
                table.HasCheckConstraint("CK_OrderItems_Quantity_Positive", "\"Quantity\" > 0");
            });
            entity.HasKey(oi => oi.Id);
            entity.Property(oi => oi.OrderId).IsRequired();
            entity.Property(oi => oi.ProductId).IsRequired();
            entity.Property(oi => oi.Quantity).IsRequired();
            entity.Property(oi => oi.UnitPrice).HasPrecision(19, 2).IsRequired();
            entity.Property(oi => oi.SubTotal).HasPrecision(19, 2).IsRequired();
        });

        // Configure Payment entity
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.OrderId).IsRequired();
            entity.Property(p => p.Amount).HasPrecision(19, 2).IsRequired();
            entity.Property(p => p.Status).HasConversion<int>().IsRequired();
            entity.Property(p => p.TransactionCode).HasMaxLength(255);
            entity.Property(p => p.RefundTransactionCode).HasMaxLength(255);
            entity.Property(p => p.IdempotencyKey).HasMaxLength(128);
            entity.Property(p => p.RefundIdempotencyKey).HasMaxLength(128);
            entity.Property(p => p.LastErrorCode).HasMaxLength(100);
            entity.Property(p => p.GatewayMessage).HasMaxLength(1000);
            entity.Property(p => p.CreatedAt).IsRequired();
            entity.Property(p => p.UpdatedAt).IsRequired();
            entity.Property(p => p.Version).IsConcurrencyToken().IsRequired();
            entity.HasIndex(p => p.OrderId).IsUnique();
            entity.HasIndex(p => p.IdempotencyKey).IsUnique();
            entity.HasIndex(p => p.RefundIdempotencyKey).IsUnique();
        });

        // Configure Product entity
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products", table =>
            {
                table.HasCheckConstraint("CK_Products_Stock_Nonnegative", "\"Stock\" >= 0");
                table.HasCheckConstraint("CK_Products_Price_Positive", "\"Price\" > 0");
            });
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(255).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(1000);
            entity.Property(p => p.Price).HasPrecision(19, 2).IsRequired();
            entity.Property(p => p.Stock).IsRequired();
            entity.Property(p => p.ImageUrl).HasMaxLength(500);
            entity.Property(p => p.Category).HasMaxLength(100);
            entity.Property(p => p.CreatedAt).IsRequired();
            entity.Property(p => p.UpdatedAt).IsRequired().IsConcurrencyToken();
            entity.Property(p => p.IsActive).IsRequired();

            // Add index for better query performance
            entity.HasIndex(p => p.Category);
            entity.HasIndex(p => p.IsActive);
        });

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Email).HasMaxLength(255).IsRequired();
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.FullName).HasMaxLength(255).IsRequired();
            entity.Property(u => u.PhoneNumber).HasMaxLength(20);
            entity.Property(u => u.Address).HasMaxLength(500);
            entity.Property(u => u.City).HasMaxLength(100);
            entity.Property(u => u.PostalCode).HasMaxLength(20);
            entity.Property(u => u.Role).HasMaxLength(50).IsRequired();
            entity.Property(u => u.IsActive).IsRequired();
            entity.Property(u => u.CreatedAt).IsRequired();
            entity.Property(u => u.UpdatedAt).IsRequired();

            // Email must be unique
            entity.HasIndex(u => u.Email).IsUnique();

            // One-to-many: User -> Orders
            entity.HasMany(u => u.Orders)
                .WithOne(o => o.User)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure ShoppingCart entity
        modelBuilder.Entity<ShoppingCart>(entity =>
        {
            entity.HasKey(sc => sc.Id);
            entity.Property(sc => sc.UserId).IsRequired();
            entity.Property(sc => sc.TotalPrice).HasPrecision(19, 2).IsRequired();
            entity.Property(sc => sc.CreatedAt).IsRequired();
            entity.Property(sc => sc.UpdatedAt).IsRequired();

            // One-to-many: ShoppingCart -> CartItems
            entity.HasMany(sc => sc.Items)
                .WithOne(ci => ci.Cart)
                .HasForeignKey(ci => ci.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-one: ShoppingCart -> User
            entity.HasOne(sc => sc.User)
                .WithMany()
                .HasForeignKey(sc => sc.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure CartItem entity
        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("CartItems", table =>
            {
                table.HasCheckConstraint("CK_CartItems_Quantity_Positive", "\"Quantity\" > 0");
            });
            entity.HasKey(ci => ci.Id);
            entity.Property(ci => ci.CartId).IsRequired();
            entity.Property(ci => ci.ProductId).IsRequired();
            entity.Property(ci => ci.Quantity).IsRequired();
            entity.Property(ci => ci.PriceAtAdd).HasPrecision(19, 2).IsRequired();
            entity.Property(ci => ci.SubTotal).HasPrecision(19, 2).IsRequired();
        });
    }
}
