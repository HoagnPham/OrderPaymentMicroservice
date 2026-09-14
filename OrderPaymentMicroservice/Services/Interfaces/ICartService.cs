using OrderPaymentMicroservice.DTOs.Cart;

namespace OrderPaymentMicroservice.Services.Interfaces;

/// <summary>
/// Shopping cart service interface
/// </summary>
public interface ICartService
{
    /// <summary>
    /// Lấy hoặc tạo giỏ hàng của user
    /// </summary>
    Task<CartResponse> GetOrCreateCartAsync(int userId);

    /// <summary>
    /// Lấy giỏ hàng theo ID
    /// </summary>
    Task<CartResponse?> GetCartByIdAsync(int cartId);

    /// <summary>
    /// Thêm item vào giỏ
    /// </summary>
    Task<CartResponse> AddItemToCartAsync(int userId, int productId, int quantity);

    /// <summary>
    /// Cập nhật số lượng item
    /// </summary>
    Task<CartResponse> UpdateCartItemAsync(int cartId, int cartItemId, int quantity);

    /// <summary>
    /// Xóa item khỏi giỏ
    /// </summary>
    Task<CartResponse> RemoveItemFromCartAsync(int cartId, int cartItemId);

    /// <summary>
    /// Xóa toàn bộ giỏ
    /// </summary>
    Task<bool> ClearCartAsync(int cartId);
}
