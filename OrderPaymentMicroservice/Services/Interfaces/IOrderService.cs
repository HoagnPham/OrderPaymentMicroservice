using OrderPaymentMicroservice.DTOs.Orders;

namespace OrderPaymentMicroservice.Services.Interfaces;

/// <summary>Order lifecycle and inventory reservation contract.</summary>
public interface IOrderService
{
    /// <summary>
    /// Create a new order
    /// TODO: Fuzzing target - validate all inputs
    /// </summary>
    Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request);

    /// <summary>
    /// Get all orders
    /// TODO: Add pagination in future
    /// </summary>
    Task<List<OrderResponse>> GetAllOrdersAsync();

    Task<List<OrderResponse>> GetOrdersByCustomerIdAsync(int customerId);

    /// <summary>
    /// Get order by ID
    /// TODO: Security test - check order exists
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
