using Microsoft.AspNetCore.Mvc;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.Services.Interfaces;

namespace OrderPaymentMicroservice.Controllers;

/// <summary>
/// Orders API Controller
/// TODO: Add authentication/authorization in future
/// TODO: Add rate limiting for security
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new order
    /// POST /api/orders
    /// </summary>
    /// <remarks>
    /// TODO: Fuzzing target - test with invalid inputs
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        try
        {
            if (request == null)
                return BadRequest("Order request cannot be null");

            _logger.LogInformation("Creating order for customer {CustomerId}", request.CustomerId);
            var response = await _orderService.CreateOrderAsync(request);
            return CreatedAtAction(nameof(GetOrder), new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Validation error: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order");
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    /// <summary>
    /// Get all orders
    /// GET /api/orders
    /// </summary>
    /// <remarks>
    /// TODO: Add pagination in future
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllOrders()
    {
        try
        {
            _logger.LogInformation("Fetching all orders");
            var orders = await _orderService.GetAllOrdersAsync();
            return Ok(orders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching orders");
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    /// <summary>
    /// Get order by ID
    /// GET /api/orders/{id}
    /// </summary>
    /// <remarks>
    /// TODO: Security test - verify order access control
    /// </remarks>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(int id)
    {
        try
        {
            _logger.LogInformation("Fetching order {OrderId}", id);
            var order = await _orderService.GetOrderByIdAsync(id);
            if (order == null)
                return NotFound($"Order {id} not found");

            return Ok(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching order {OrderId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    /// <summary>
    /// Cancel an order
    /// POST /api/orders/{id}/cancel
    /// </summary>
    /// <remarks>
    /// TODO: Kripke/BMC - verify state transition validity
    /// </remarks>
    [HttpPost("{id}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelOrder(int id)
    {
        try
        {
            _logger.LogInformation("Cancelling order {OrderId}", id);
            var result = await _orderService.CancelOrderAsync(id);
            if (!result)
                return BadRequest($"Cannot cancel order {id}. Order may not exist or cannot be cancelled in its current state.");

            return Ok(new { message = $"Order {id} cancelled successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling order {OrderId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }
}
