using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderPaymentMicroservice.DTOs.Cart;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services.Interfaces;

namespace OrderPaymentMicroservice.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;
    private readonly ILogger<CartController> _logger;

    public CartController(ICartService cartService, ILogger<CartController> logger)
    {
        _cartService = cartService;
        _logger = logger;
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetCart(int userId)
    {
        try
        {
            if (!User.CanAccessUser(userId)) return Forbid();
            var cart = await _cartService.GetOrCreateCartAsync(userId);
            return Ok(cart);
        }
        catch (DbUpdateException)
        {
            return Conflict("Cart changed concurrently; reload and retry");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching cart for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    [HttpPost("user/{userId}/add")]
    public async Task<IActionResult> AddToCart(int userId, [FromBody] AddToCartRequest request)
    {
        try
        {
            if (!User.CanAccessUser(userId)) return Forbid();
            var cart = await _cartService.AddItemToCartAsync(userId, request.ProductId, request.Quantity);
            return Ok(cart);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (DbUpdateException)
        {
            return Conflict("Cart changed concurrently; reload and retry");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding item to cart");
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    [HttpPut("{cartId}/item/{itemId}")]
    public async Task<IActionResult> UpdateCartItem(int cartId, int itemId, [FromBody] UpdateCartItemRequest request)
    {
        try
        {
            var cart = await _cartService.GetCartByIdAsync(cartId);
            if (cart == null) return NotFound($"Cart {cartId} not found");
            if (!User.CanAccessUser(cart.UserId)) return Forbid();
            var updatedCart = await _cartService.UpdateCartItemAsync(cartId, itemId, request.Quantity);
            return Ok(updatedCart);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (DbUpdateException)
        {
            return Conflict("Cart changed concurrently; reload and retry");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating cart item");
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    [HttpDelete("{cartId}/item/{itemId}")]
    public async Task<IActionResult> RemoveFromCart(int cartId, int itemId)
    {
        try
        {
            var existingCart = await _cartService.GetCartByIdAsync(cartId);
            if (existingCart == null) return NotFound($"Cart {cartId} not found");
            if (!User.CanAccessUser(existingCart.UserId)) return Forbid();
            var cart = await _cartService.RemoveItemFromCartAsync(cartId, itemId);
            return Ok(cart);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
        catch (DbUpdateException)
        {
            return Conflict("Cart changed concurrently; reload and retry");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing item from cart");
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    [HttpDelete("{cartId}/clear")]
    public async Task<IActionResult> ClearCart(int cartId)
    {
        try
        {
            var cart = await _cartService.GetCartByIdAsync(cartId);
            if (cart == null) return NotFound($"Cart {cartId} not found");
            if (!User.CanAccessUser(cart.UserId)) return Forbid();
            await _cartService.ClearCartAsync(cartId);
            return Ok(new { message = "Cart cleared successfully" });
        }
        catch (DbUpdateException)
        {
            return Conflict("Cart changed concurrently; reload and retry");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cart");
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }
}
