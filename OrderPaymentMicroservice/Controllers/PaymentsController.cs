using Microsoft.AspNetCore.Mvc;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.Services.Interfaces;

namespace OrderPaymentMicroservice.Controllers;

/// <summary>
/// Payments API Controller
/// TODO: Add authentication/authorization in future
/// TODO: Add idempotency key for payment requests
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(IPaymentService paymentService, ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
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
    public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentRequest request)
    {
        try
        {
            if (request == null)
                return BadRequest("Payment request cannot be null");

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
            return NotFound(ex.Message);
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

            return Ok(payment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching payment {PaymentId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }
}
