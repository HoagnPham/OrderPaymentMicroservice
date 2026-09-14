using OrderPaymentMicroservice.DTOs.Products;

namespace OrderPaymentMicroservice.Services.Interfaces;

/// <summary>
/// Product service interface
/// </summary>
public interface IProductService
{
    /// <summary>
    /// Lấy tất cả products
    /// </summary>
    Task<List<ProductResponse>> GetAllProductsAsync();

    /// <summary>
    /// Lấy products theo category
    /// </summary>
    Task<List<ProductResponse>> GetProductsByCategoryAsync(string category);

    /// <summary>
    /// Lấy product theo ID
    /// </summary>
    Task<ProductResponse?> GetProductByIdAsync(int productId);

    /// <summary>
    /// Tạo product mới
    /// </summary>
    Task<ProductResponse> CreateProductAsync(CreateProductRequest request);

    /// <summary>
    /// Cập nhật product
    /// </summary>
    Task<ProductResponse?> UpdateProductAsync(int productId, UpdateProductRequest request);

    /// <summary>
    /// Xóa product
    /// </summary>
    Task<bool> DeleteProductAsync(int productId);
}
