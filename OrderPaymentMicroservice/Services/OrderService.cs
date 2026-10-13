using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace OrderPaymentMicroservice.Services;

/// <summary>Owns the durable order lifecycle and inventory reservation.</summary>
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
    public async Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request, string? idempotencyKey = null)
    {
        _validator.ValidateCreateOrderRequest(request);
        if (idempotencyKey != null && (idempotencyKey.Length is < 1 or > 128 ||
            idempotencyKey.Any(c => c < '!' || c > '~')))
            throw new ArgumentException("Idempotency-Key must contain 1 to 128 visible ASCII characters");
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var keyHash = idempotencyKey == null ? null : Hash(idempotencyKey);
        var requestHash = keyHash == null ? null : Hash(JsonSerializer.Serialize(new
        {
            request.CustomerId, TotalAmount = request.TotalAmount.ToString("G29", CultureInfo.InvariantCulture),
            request.Address, request.City,
            Items = request.Items.Select(i => new { i.ProductId, i.Quantity, UnitPrice = i.UnitPrice.ToString("G29", CultureInfo.InvariantCulture) })
        }));

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;

        if (keyHash != null)
        {
            var previous = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o =>
                o.CustomerId == request.CustomerId && o.CreationKeyHash == keyHash);
            if (previous != null)
            {
                if (previous.CreationRequestHash != requestHash)
                    throw new OrderCreationConflictException("IDEMPOTENCY_PAYLOAD_MISMATCH", "Idempotency-Key was already used for a different order request");
                return JsonSerializer.Deserialize<OrderResponse>(previous.CreationResponseJson
                    ?? throw new OrderCreationConflictException("ORDER_RECEIPT_UNAVAILABLE", "Order creation receipt is incomplete"))!;
            }
        }

        // Stock must cover the sum of all lines for a product, not each line separately.
        var requestedQuantities = request.Items.GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.Quantity));
        var productIds = requestedQuantities.Keys.ToList();
        var products = await _context.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);

        if (products.Count != productIds.Count)
            throw new OrderCreationConflictException("PRODUCT_UNAVAILABLE", "One or more products do not exist");

        decimal serverTotal = 0;
        foreach (var item in request.Items)
        {
            var product = products[item.ProductId];
            MoneyAmounts.RequireCents(product.Price, nameof(product.Price));
            if (!product.IsActive)
                throw new OrderCreationConflictException("PRODUCT_UNAVAILABLE", $"Product {product.Id} is not active");
            if (product.Stock < requestedQuantities[item.ProductId])
                throw new OrderCreationConflictException("OUT_OF_STOCK", $"Not enough stock for product {product.Id}");

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
            CreationKeyHash = keyHash,
            CreationRequestHash = requestHash,
            UserId = request.CustomerId,   // Controller overwrites CustomerId from the authenticated principal.
            TotalAmount = serverTotal,
            Address = request.Address,
            City = request.City,
            Status = OrderStatus.CREATED,
            InventoryStatus = InventoryReservationStatus.RESERVED,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
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

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        var response = MapToResponse(order);
        if (keyHash != null)
        {
            order.CreationResponseJson = JsonSerializer.Serialize(response);
            await _context.SaveChangesAsync();
        }
        if (transaction != null)
            await transaction.CommitAsync();

        return response;
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
    /// Cancellation is legal only before a charge can be successful or unknown.
    /// </summary>
    public async Task<bool> CancelOrderAsync(int orderId)
    {
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;

        var order = await _context.Orders
            .Include(candidate => candidate.OrderItems)
            .Include(candidate => candidate.Payments)
            .FirstOrDefaultAsync(candidate => candidate.Id == orderId);
        if (order == null)
            return false;

        if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.CANCELLED))
            return false;

        if (order.Payments.Any(payment => payment.Status is
                PaymentStatus.PROCESSING or PaymentStatus.UNKNOWN or PaymentStatus.SUCCESS or PaymentStatus.REFUND_PENDING))
            return false;

        OrderStateMachine.Transition(order, OrderStatus.CANCELLED);
        await InventoryReservation.ReleaseAsync(_context, order);
        await _context.SaveChangesAsync();
        if (transaction != null)
            await transaction.CommitAsync();
        return true;
    }

    public async Task<OrderResponse> ConfirmOrderAsync(int orderId)
    {
        var order = await LoadOrderAggregateAsync(orderId)
            ?? throw new InvalidOperationException($"Order {orderId} not found");

        if (!order.Payments.Any(payment => payment.Status == PaymentStatus.SUCCESS))
            throw new InvalidOperationException("Order cannot be confirmed without a successful payment");

        OrderStateMachine.Transition(order, OrderStatus.CONFIRMED);
        await _context.SaveChangesAsync();
        return MapToResponse(order);
    }

    public async Task<OrderResponse> CompleteOrderAsync(int orderId)
    {
        var order = await LoadOrderAggregateAsync(orderId)
            ?? throw new InvalidOperationException($"Order {orderId} not found");

        if (!order.Payments.Any(payment => payment.Status == PaymentStatus.SUCCESS))
            throw new InvalidOperationException("Order cannot be completed without a successful payment");

        OrderStateMachine.Transition(order, OrderStatus.COMPLETED);
        if (order.InventoryStatus != InventoryReservationStatus.RESERVED)
            throw new InvalidOperationException($"Order inventory is {order.InventoryStatus}, expected RESERVED");
        order.InventoryStatus = InventoryReservationStatus.CONSUMED;
        await _context.SaveChangesAsync();
        return MapToResponse(order);
    }

    private Task<Order?> LoadOrderAggregateAsync(int orderId) => _context.Orders
        .Include(order => order.OrderItems)
            .ThenInclude(item => item.Product)
        .Include(order => order.Payments)
        .FirstOrDefaultAsync(order => order.Id == orderId);

    private OrderResponse MapToResponse(Order order)
    {
        return new OrderResponse
        {
            Id = order.Id,
            CustomerId = order.CustomerId,
            TotalAmount = order.TotalAmount,
            Status = order.Status.ToString(),
            InventoryStatus = order.InventoryStatus.ToString(),
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
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
