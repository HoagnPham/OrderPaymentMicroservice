using OrderPaymentMicroservice.DTOs.Orders;

namespace OrderPaymentMicroservice.Services.Interfaces;

/// <summary>Order lifecycle and inventory reservation contract.</summary>
public interface IOrderService
{
    /// <summary>
    /// Create a new order. Input bounds are checked by InputValidator and fuzz campaigns.
    /// </summary>
    Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request, string? idempotencyKey = null);

    /// <summary>
    /// Get all orders. Known limitation: no pagination or filtering yet.
    /// </summary>
    Task<List<OrderResponse>> GetAllOrdersAsync();

    Task<List<OrderResponse>> GetOrdersByCustomerIdAsync(int customerId);

    /// <summary>
    /// Get order by ID; callers enforce ownership before returning the response.
    /// </summary>
    Task<OrderResponse?> GetOrderByIdAsync(int orderId);

    /// <summary>
    /// Cancel an order
    /// Cancels only a pre-charge order and releases inventory idempotently.
    /// </summary>
    Task<bool> CancelOrderAsync(int orderId);

    Task<OrderResponse> ConfirmOrderAsync(int orderId);

    Task<OrderResponse> CompleteOrderAsync(int orderId);
}
