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
        _validator.ValidateCreateOrderRequest(request);

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;

        var productIds = request.Items.Select(item => item.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);

        if (products.Count != productIds.Count)
            throw new InvalidOperationException("One or more products do not exist");

        decimal serverTotal = 0;
        foreach (var item in request.Items)
        {
            var product = products[item.ProductId];
            if (!product.IsActive)
                throw new InvalidOperationException($"Product {product.Id} is not active");
            if (product.Stock < item.Quantity)
                throw new InvalidOperationException($"Not enough stock for product {product.Id}");

            serverTotal = checked(serverTotal + item.Quantity * product.Price);
        }

        if (Math.Abs(request.TotalAmount - serverTotal) > 0.01m)
            throw new ArgumentException("TotalAmount does not match current product prices", nameof(request.TotalAmount));

        foreach (var item in request.Items)
        {
            var product = products[item.ProductId];
            product.Stock -= item.Quantity;
            product.UpdatedAt = DateTime.UtcNow;
        }

        // Create order
        var order = new Order
        {
            CustomerId = request.CustomerId,
            UserId = request.CustomerId,   // dùng CustomerId làm UserId (chưa có JWT)
            TotalAmount = serverTotal,
            Address = request.Address,
            City = request.City,
            Status = OrderStatus.CREATED,
            CreatedAt = DateTime.UtcNow
        };

        // Create order items
        foreach (var itemRequest in request.Items)
        {
            var unitPrice = products[itemRequest.ProductId].Price;
            var subtotal = checked(itemRequest.Quantity * unitPrice);
            order.OrderItems.Add(new OrderItem
            {
                ProductId = itemRequest.ProductId,
                Quantity = itemRequest.Quantity,
                UnitPrice = unitPrice,
                SubTotal = subtotal
            });
        }

        OrderStateMachine.Transition(order, OrderStatus.VALIDATED);
        OrderStateMachine.Transition(order, OrderStatus.PAYMENT_PENDING);

        // Save to database (skeleton - may not be persisted in earliest version)
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        if (transaction != null)
            await transaction.CommitAsync();

        return MapToResponse(order);
    }

    /// <summary>
    /// Get all orders
    /// TODO: Add pagination, filtering in future
    /// </summary>
    public async Task<List<OrderResponse>> GetAllOrdersAsync()
    {
        var orders = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .ToListAsync();
        return orders.Select(MapToResponse).ToList();
    }

    public async Task<List<OrderResponse>> GetOrdersByCustomerIdAsync(int customerId)
    {
        var orders = await _context.Orders
            .Where(order => order.CustomerId == customerId)
            .Include(order => order.OrderItems)
                .ThenInclude(item => item.Product)
            .ToListAsync();
        return orders.Select(MapToResponse).ToList();
    }

    /// <summary>
    /// Get order by ID
    /// TODO: Security test - verify order access
    /// </summary>
    public async Task<OrderResponse?> GetOrderByIdAsync(int orderId)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);
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

        if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.CANCELLED))
            return false;

        OrderStateMachine.Transition(order, OrderStatus.CANCELLED);
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
            Address = order.Address,
            City = order.City,
            Items = order.OrderItems.Select(oi => new OrderItemResponse
            {
                Id = oi.Id,
                ProductId = oi.ProductId,
                ProductName = oi.Product?.Name ?? $"Product #{oi.ProductId}",
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                SubTotal = oi.SubTotal
            }).ToList()
        };
    }
}
