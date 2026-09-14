using OrderPaymentMicroservice.DTOs.Orders;

namespace OrderPaymentMicroservice.Services.Interfaces;

/// <summary>
/// Order service interface
/// TODO: Kripke/BMC - define order lifecycle contracts
/// TODO: SMT - define amount and state transition constraints
/// </summary>
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

    /// <summary>
    /// Get order by ID
    /// TODO: Security test - check order exists
    /// </summary>
    Task<OrderResponse?> GetOrderByIdAsync(int orderId);

    /// <summary>
    /// Cancel an order
    /// TODO: Kripke/BMC - verify order can be cancelled from current state
    /// </summary>
    Task<bool> CancelOrderAsync(int orderId);
}
