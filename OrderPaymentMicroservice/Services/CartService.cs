using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Cart;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Services.Interfaces;

namespace OrderPaymentMicroservice.Services;

public class CartService : ICartService
{
    private readonly AppDbContext _context;
    private readonly ILogger<CartService> _logger;

    public CartService(AppDbContext context, ILogger<CartService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CartResponse> GetOrCreateCartAsync(int userId)
    {
        var cart = await _context.ShoppingCarts
            .Include(c => c.Items)
            .ThenInclude(ci => ci.Product)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart == null)
        {
            cart = new ShoppingCart
            {
                UserId = userId,
                TotalPrice = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.ShoppingCarts.Add(cart);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Cart created for user: {UserId}", userId);
        }

        return MapToResponse(cart);
    }

    public async Task<CartResponse?> GetCartByIdAsync(int cartId)
    {
        var cart = await _context.ShoppingCarts
            .Include(c => c.Items)
            .ThenInclude(ci => ci.Product)
            .FirstOrDefaultAsync(c => c.Id == cartId);

        return cart != null ? MapToResponse(cart) : null;
    }

    public async Task<CartResponse> AddItemToCartAsync(int userId, int productId, int quantity)
    {
        if (userId <= 0 || productId <= 0)
            throw new InvalidOperationException("UserId and ProductId must be positive");
        if (quantity < 1 || quantity > 10000)
            throw new InvalidOperationException("Quantity must be between 1 and 10000");
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;
        var product = await _context.Products.FindAsync(productId);

        if (product == null || !product.IsActive)
            throw new InvalidOperationException($"Product {productId} is not available");
        var cart = await GetOrCreateCartAsync(userId);

        if (product.Stock < quantity)
            throw new InvalidOperationException($"Not enough stock for product {productId}");

        var existingItem = await _context.CartItems
            .FirstOrDefaultAsync(ci => ci.CartId == cart.Id && ci.ProductId == productId);

        var combinedQuantity = (long)(existingItem?.Quantity ?? 0) + quantity;
        if (combinedQuantity > 10000 || combinedQuantity > product.Stock)
            throw new InvalidOperationException("Combined quantity exceeds stock or the quantity limit");
        if (existingItem != null)
        {
            existingItem.Quantity = (int)combinedQuantity;
            existingItem.SubTotal = existingItem.Quantity * existingItem.PriceAtAdd;
        }
        else
        {
            var cartItem = new CartItem
            {
                CartId = cart.Id,
                ProductId = productId,
                Quantity = quantity,
                PriceAtAdd = product.Price,
                SubTotal = quantity * product.Price
            };

            _context.CartItems.Add(cartItem);
        }

        // Lưu CartItem trước, sau đó mới tính lại tổng
        await _context.SaveChangesAsync();
        await UpdateCartTotalAsync(cart.Id);
        if (transaction != null) await transaction.CommitAsync();
        _logger.LogInformation("Item added to cart: User={UserId}, Product={ProductId}, Qty={Quantity}", userId, productId, quantity);

        var updatedCart = await GetCartByIdAsync(cart.Id);
        return updatedCart ?? throw new InvalidOperationException("Cart not found after adding item");
    }

    public async Task<CartResponse> UpdateCartItemAsync(int cartId, int cartItemId, int quantity)
    {
        if (quantity < 0 || quantity > 10000)
            throw new InvalidOperationException("Quantity must be between 0 and 10000; zero removes the item");
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;
        var cartItem = await _context.CartItems.FindAsync(cartItemId);
        if (cartItem == null || cartItem.CartId != cartId)
            throw new InvalidOperationException("Cart item not found");

        if (quantity == 0)
        {
            var result = await RemoveItemFromCartAsync(cartId, cartItemId);
            if (transaction != null) await transaction.CommitAsync();
            return result;
        }
        var product = await _context.Products.FindAsync(cartItem.ProductId);
        if (product == null || !product.IsActive || quantity > product.Stock)
            throw new InvalidOperationException("Product is unavailable or quantity exceeds stock");

        cartItem.Quantity = quantity;
        cartItem.SubTotal = quantity * cartItem.PriceAtAdd;
        await _context.SaveChangesAsync();

        await UpdateCartTotalAsync(cartId);
        if (transaction != null) await transaction.CommitAsync();
        return await GetCartByIdAsync(cartId) ?? throw new InvalidOperationException("Cart not found");
    }

    public async Task<CartResponse> RemoveItemFromCartAsync(int cartId, int cartItemId)
    {
        // Reuse an update transaction when quantity zero delegates to removal.
        await using var transaction = _context.Database.IsRelational() && _context.Database.CurrentTransaction == null
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;
        var cartItem = await _context.CartItems.FindAsync(cartItemId);
        if (cartItem == null || cartItem.CartId != cartId)
            throw new InvalidOperationException("Cart item not found");

        _context.CartItems.Remove(cartItem);
        await _context.SaveChangesAsync();

        await UpdateCartTotalAsync(cartId);
        if (transaction != null) await transaction.CommitAsync();
        return await GetCartByIdAsync(cartId) ?? throw new InvalidOperationException("Cart not found");
    }

    public async Task<bool> ClearCartAsync(int cartId)
    {
        var cartItems = await _context.CartItems.Where(ci => ci.CartId == cartId).ToListAsync();
        _context.CartItems.RemoveRange(cartItems);

        var cart = await _context.ShoppingCarts.FindAsync(cartId);
        if (cart != null)
        {
            cart.TotalPrice = 0;
            cart.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    private async Task UpdateCartTotalAsync(int cartId)
    {
        var cart = await _context.ShoppingCarts.FindAsync(cartId);
        if (cart == null) return;

        var total = await _context.CartItems
            .Where(ci => ci.CartId == cartId)
            .SumAsync(ci => ci.SubTotal);

        cart.TotalPrice = total;
        cart.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private CartResponse MapToResponse(ShoppingCart cart)
    {
        return new CartResponse
        {
            Id = cart.Id,
            UserId = cart.UserId,
            TotalPrice = cart.TotalPrice,
            Items = cart.Items.Select(ci => new CartItemResponse
            {
                Id = ci.Id,
                ProductId = ci.ProductId,
                ProductName = ci.Product?.Name ?? "Unknown",
                Quantity = ci.Quantity,
                PriceAtAdd = ci.PriceAtAdd,
                SubTotal = ci.SubTotal
            }).ToList(),
            UpdatedAt = cart.UpdatedAt
        };
    }
}
