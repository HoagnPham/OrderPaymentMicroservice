using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services.Interfaces;

namespace OrderPaymentMicroservice.Controllers;

/// <summary>Authenticated payment, retry and compensation API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IOrderService _orderService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentService paymentService,
        IOrderService orderService,
        ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
        _orderService = orderService;
        _logger = logger;
    }

    /// <summary>
    /// Create a payment for an order
    /// POST /api/payments
    /// </summary>
    /// <remarks>
    /// TODO: Fuzzing target - test with various payment amounts
    /// TODO: SMT verification - amount constraints
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePayment(
        [FromBody] CreatePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        try
        {
            if (request == null)
                return BadRequest("Payment request cannot be null");
            request.IdempotencyKey = idempotencyKey?.Trim() ?? string.Empty;

            var order = await _orderService.GetOrderByIdAsync(request.OrderId);
            if (order == null)
                return NotFound($"Order {request.OrderId} not found");
            if (!User.CanAccessUser(order.CustomerId))
                return Forbid();

            _logger.LogInformation("Creating payment for order {OrderId} with amount {Amount}", request.OrderId, request.Amount);
            var response = await _paymentService.CreatePaymentAsync(request);
            return CreatedAtAction(nameof(GetPayment), new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Validation error: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Operation error: {Message}", ex.Message);
            return Conflict(ex.Message);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent payment conflict");
            return Conflict("Payment or order state changed concurrently; reload and reconcile");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating payment");
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    /// <summary>
    /// Get payment by ID
    /// GET /api/payments/{id}
    /// </summary>
    /// <remarks>
    /// TODO: Security test - check payment access control
    /// </remarks>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayment(int id)
    {
        try
        {
            _logger.LogInformation("Fetching payment {PaymentId}", id);
            var payment = await _paymentService.GetPaymentByIdAsync(id);
            if (payment == null)
                return NotFound($"Payment {id} not found");

            var order = await _orderService.GetOrderByIdAsync(payment.OrderId);
            if (order == null)
                return NotFound($"Order {payment.OrderId} not found");
            if (!User.CanAccessUser(order.CustomerId))
                return Forbid();

            return Ok(payment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching payment {PaymentId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    [HttpPost("{id}/retry")]
    public async Task<IActionResult> RetryPayment(int id)
    {
        try
        {
            var access = await CheckPaymentAccessAsync(id);
            if (access != null) return access;
            return Ok(await _paymentService.RetryPaymentAsync(id));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (DbUpdateException)
        {
            return Conflict("Payment state changed concurrently; reload and reconcile");
        }
    }

    [HttpPost("{id}/reconcile")]
    public async Task<IActionResult> ReconcilePayment(int id)
    {
        try
        {
            var access = await CheckPaymentAccessAsync(id);
            if (access != null) return access;
            return Ok(await _paymentService.ReconcilePaymentAsync(id));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (DbUpdateException)
        {
            return Conflict("Payment state changed concurrently; reload and reconcile");
        }
    }

    [HttpPost("{id}/refund")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RefundPayment(
        int id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        try
        {
            return Ok(await _paymentService.RefundPaymentAsync(id, idempotencyKey?.Trim() ?? string.Empty));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (DbUpdateException)
        {
            return Conflict("Refund state changed concurrently; reload and reconcile");
        }
    }

    [HttpPost("{id}/refund/retry")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RetryRefund(int id)
    {
        try
        {
            return Ok(await _paymentService.RetryRefundAsync(id));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (DbUpdateException)
        {
            return Conflict("Refund state changed concurrently; reload and reconcile");
        }
    }

    [HttpPost("{id}/refund/reconcile")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ReconcileRefund(int id)
    {
        try
        {
            return Ok(await _paymentService.ReconcileRefundAsync(id));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (DbUpdateException)
        {
            return Conflict("Refund state changed concurrently; reload and reconcile");
        }
    }

    private async Task<IActionResult?> CheckPaymentAccessAsync(int id)
    {
        var payment = await _paymentService.GetPaymentByIdAsync(id);
        if (payment == null) return NotFound($"Payment {id} not found");
        var order = await _orderService.GetOrderByIdAsync(payment.OrderId);
        if (order == null) return NotFound($"Order {payment.OrderId} not found");
        return User.CanAccessUser(order.CustomerId) ? null : Forbid();
    }
}
