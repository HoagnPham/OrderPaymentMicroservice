using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Models;

namespace OrderPaymentMicroservice.Data;

/// <summary>Entity Framework Core persistence model and database invariants.</summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
    public DbSet<Payment> Payments { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<ShoppingCart> ShoppingCarts { get; set; } = null!;
    public DbSet<CartItem> CartItems { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Order entity
        modelBuilder.Entity<Order>(entity =>
        {
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
            entity.HasKey(ci => ci.Id);
            entity.Property(ci => ci.CartId).IsRequired();
            entity.Property(ci => ci.ProductId).IsRequired();
            entity.Property(ci => ci.Quantity).IsRequired();
            entity.Property(ci => ci.PriceAtAdd).HasPrecision(19, 2).IsRequired();
            entity.Property(ci => ci.SubTotal).HasPrecision(19, 2).IsRequired();
        });
    }
}
