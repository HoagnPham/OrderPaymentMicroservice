using OrderPaymentMicroservice.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services.Interfaces;
using OrderPaymentMicroservice.Services;

namespace OrderPaymentMicroservice.Controllers;

/// <summary>Authenticated order lifecycle API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
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
    /// Invalid-input cases are covered by RequestBoundaryRegressionTests and SecurityFuzzingTests.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest? request)
    {
        try
        {
            if (request == null)
                return BadRequest("Order request cannot be null");
            request.CustomerId = User.GetRequiredUserId();

            _logger.LogInformation("Creating order for customer {CustomerId}", request.CustomerId);
            var keys = Request.Headers["Idempotency-Key"];
            if (keys.Count > 1) return BadRequest("Provide exactly one Idempotency-Key");
            var response = await _orderService.CreateOrderAsync(request, keys.Count == 0 ? null : keys[0]);
            return CreatedAtAction(nameof(GetOrder), new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Validation error: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (OrderCreationConflictException ex)
        {
            return Conflict(new {code=ex.Code,retryable=false,message=ex.Message});
        }
        catch (Exception ex) when (PersistenceConflicts.IsConcurrentWrite(ex)||PersistenceConflicts.IsOrderCreationCollision(ex))
        {
            var keys=Request.Headers["Idempotency-Key"];
            var retryable=keys.Count==1 && keys[0] is {Length:>0 and <=128} key && key.All(c=>c>='!'&&c<='~');
            return Conflict(new {code="ORDER_CONCURRENT_WRITE",retryable,message=retryable
                ? "Order data changed concurrently; retry with the same Idempotency-Key and payload"
                : "Order data changed concurrently; reload before submitting again"});
        }
        catch (InvalidOperationException)
        {
            return Conflict(new {code="ORDER_REJECTED",retryable=false,message="Order could not be created; check current order and product data"});
        }
        catch (DbUpdateException)
        {
            return Conflict(new {code="ORDER_PERSISTENCE_REJECTED",retryable=false,message="Order data could not be saved; check current order and product data"});
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
    /// Known limitation: returns the full order list; pagination is future work.
    /// </remarks>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllOrders()
    {
        try
        {
            _logger.LogInformation("Fetching all orders");
            var orders = await _orderService.GetAllOrdersAsync();
            return Ok(orders);
        }
        catch (Exception ex) when (PersistenceConflicts.IsConcurrentWrite(ex))
        {
            return Conflict("Concurrent transaction conflict; reload and reconcile before retrying");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching orders");
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    [HttpGet("mine")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyOrders()
    {
        var orders = await _orderService.GetOrdersByCustomerIdAsync(User.GetRequiredUserId());
        return Ok(orders);
    }

    /// <summary>
    /// Get order by ID
    /// GET /api/orders/{id}
    /// </summary>
    /// <remarks>
    /// Access control: User.CanAccessUser is enforced below and covered by security tests.
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
            if (!User.CanAccessUser(order.CustomerId))
                return Forbid();

            return Ok(order);
        }
        catch (Exception ex) when (PersistenceConflicts.IsConcurrentWrite(ex))
        {
            return Conflict("Concurrent transaction conflict; reload and reconcile before retrying");
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
    /// The service rejects cancellation after payment starts processing.
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
            var order = await _orderService.GetOrderByIdAsync(id);
            if (order == null)
                return NotFound($"Order {id} not found");
            if (!User.CanAccessUser(order.CustomerId))
                return Forbid();
            var result = await _orderService.CancelOrderAsync(id);
            if (!result)
                return BadRequest($"Cannot cancel order {id}. Order may not exist or cannot be cancelled in its current state.");

            return Ok(new { message = $"Order {id} cancelled successfully" });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent cancellation conflict for order {OrderId}", id);
            return Conflict("Order or inventory changed concurrently; reload and retry");
        }
        catch (Exception ex) when (PersistenceConflicts.IsConcurrentWrite(ex))
        {
            return Conflict("Concurrent transaction conflict; reload and reconcile before retrying");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling order {OrderId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    [HttpPost("{id}/confirm")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ConfirmOrder(int id)
    {
        try
        {
            return Ok(await _orderService.ConfirmOrderAsync(id));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (DbUpdateException)
        {
            return Conflict("Order state changed concurrently; reload and retry");
        }
    }

    [HttpPost("{id}/complete")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CompleteOrder(int id)
    {
        try
        {
            return Ok(await _orderService.CompleteOrderAsync(id));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (DbUpdateException)
        {
            return Conflict("Order state changed concurrently; reload and retry");
        }
    }
}
