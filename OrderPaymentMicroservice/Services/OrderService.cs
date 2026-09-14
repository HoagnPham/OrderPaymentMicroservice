using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services.Interfaces;

namespace OrderPaymentMicroservice.Services;

/// <summary>
/// Order service implementation - skeleton version
/// TODO: Implement full business logic after skeleton is approved
/// TODO: Kripke/BMC - add state machine verification
/// </summary>
public class OrderService : IOrderService
{
    private readonly AppDbContext _context;
    private readonly InputValidator _validator;

    public OrderService(AppDbContext context, InputValidator validator)
    {
        _context = context;
        _validator = validator;
    }

    /// <summary>
    /// Create a new order
    /// TODO: Fuzzing target - test with invalid inputs
    /// </summary>
    public async Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request)
    {
        // Validate input
        _validator.ValidateCreateOrderRequest(request);

        // Create order
        var order = new Order
        {
            CustomerId = request.CustomerId,
            TotalAmount = request.TotalAmount,
            Status = OrderStatus.CREATED,
            CreatedAt = DateTime.UtcNow
        };

        // Create order items
        foreach (var itemRequest in request.Items)
        {
            var subtotal = itemRequest.Quantity * itemRequest.UnitPrice;
            order.OrderItems.Add(new OrderItem
            {
                ProductId = itemRequest.ProductId,
                Quantity = itemRequest.Quantity,
                UnitPrice = itemRequest.UnitPrice,
                SubTotal = subtotal
            });
        }

        // Save to database (skeleton - may not be persisted in earliest version)
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        return MapToResponse(order);
    }

    /// <summary>
    /// Get all orders
    /// TODO: Add pagination, filtering in future
    /// </summary>
    public async Task<List<OrderResponse>> GetAllOrdersAsync()
    {
        var orders = await _context.Orders.ToListAsync();
        return orders.Select(MapToResponse).ToList();
    }

    /// <summary>
    /// Get order by ID
    /// TODO: Security test - verify order access
    /// </summary>
    public async Task<OrderResponse?> GetOrderByIdAsync(int orderId)
    {
        var order = await _context.Orders.FindAsync(orderId);
        return order != null ? MapToResponse(order) : null;
    }

    /// <summary>
    /// Cancel an order
    /// TODO: Kripke/BMC - verify order can transition to CANCELLED from current state
    /// </summary>
    public async Task<bool> CancelOrderAsync(int orderId)
    {
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null)
            return false;

        // Simple validation - can only cancel if not already completed/paid
        if (order.Status == OrderStatus.COMPLETED || order.Status == OrderStatus.PAID)
            return false;

        order.Status = OrderStatus.CANCELLED;
        await _context.SaveChangesAsync();
        return true;
    }

    private OrderResponse MapToResponse(Order order)
    {
        return new OrderResponse
        {
            Id = order.Id,
            CustomerId = order.CustomerId,
            TotalAmount = order.TotalAmount,
            Status = order.Status.ToString(),
            CreatedAt = order.CreatedAt,
            Items = order.OrderItems.Select(oi => new OrderItemResponse
            {
                Id = oi.Id,
                ProductId = oi.ProductId,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                SubTotal = oi.SubTotal
            }).ToList()
        };
    }
}
