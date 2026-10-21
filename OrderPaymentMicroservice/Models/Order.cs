namespace OrderPaymentMicroservice.Models;

/// <summary>Order aggregate root.</summary>
public class Order
{
    public int Id { get; set; }

    public string? CreationKeyHash { get; set; }
    public string? CreationRequestHash { get; set; }
    public string? CreationResponseJson { get; set; }

    /// <summary>
    /// User ID (khách hàng)
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Customer ID; OrdersController overwrites it with the authenticated user id,
    /// so ownership does not depend on client-supplied input.
    /// </summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// Total order amount; > 0 enforced by InputValidator and modelled by
    /// PaymentVerification.z3 TEST1/TEST2.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Current durable business status; mutations go through OrderStateMachine.
    /// </summary>
    public OrderStatus Status { get; set; } = OrderStatus.CREATED;

    public InventoryReservationStatus InventoryStatus { get; set; } = InventoryReservationStatus.RESERVED;

    /// <summary>
    /// Order creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public long Version { get; set; }

    /// <summary>
    /// Delivery address
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Delivery city
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Order items (1-to-many relationship)
    /// </summary>
    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    /// <summary>
    /// Order payments (1-to-many relationship)
    /// </summary>
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    /// <summary>
    /// Navigation property to User
    /// </summary>
    public virtual User? User { get; set; }
}
