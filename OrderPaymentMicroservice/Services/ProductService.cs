using OrderPaymentMicroservice.Security;
using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Products;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Services.Interfaces;

namespace OrderPaymentMicroservice.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ProductService> _logger;

    public ProductService(AppDbContext context, ILogger<ProductService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<ProductResponse>> GetAllProductsAsync()
    {
        var products = await _context.Products
            .Where(p => p.IsActive)
            .ToListAsync();
        return products.Select(MapToResponse).ToList();
    }

    public async Task<List<ProductResponse>> GetProductsByCategoryAsync(string category)
    {
        var products = await _context.Products
            .Where(p => p.IsActive && p.Category == category)
            .ToListAsync();
        return products.Select(MapToResponse).ToList();
    }

    public async Task<ProductResponse?> GetProductByIdAsync(int productId)
    {
        var product = await _context.Products.FindAsync(productId);
        return product?.IsActive == true ? MapToResponse(product) : null;
    }

    public async Task<ProductResponse> CreateProductAsync(CreateProductRequest request)
    {
        RequestValidation.Validate(request);
        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Stock = request.Stock,
            ImageUrl = request.ImageUrl,
            Category = request.Category,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Product created: {ProductId}", product.Id);
        return MapToResponse(product);
    }

    public async Task<ProductResponse?> UpdateProductAsync(int productId, UpdateProductRequest request)
    {
        RequestValidation.Validate(request);
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
            return null;

        if (!string.IsNullOrEmpty(request.Name))
            product.Name = request.Name;
        if (!string.IsNullOrEmpty(request.Description))
            product.Description = request.Description;
        if (request.Price.HasValue)
            product.Price = request.Price.Value;
        if (request.Stock.HasValue)
            product.Stock = request.Stock.Value;
        if (!string.IsNullOrEmpty(request.ImageUrl))
            product.ImageUrl = request.ImageUrl;
        if (!string.IsNullOrEmpty(request.Category))
            product.Category = request.Category;

        product.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Product updated: {ProductId}", productId);
        return MapToResponse(product);
    }

    public async Task<bool> DeleteProductAsync(int productId)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
            return false;

        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Product deleted: {ProductId}", productId);
        return true;
    }

    private ProductResponse MapToResponse(Product product)
    {
        return new ProductResponse
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            Stock = product.Stock,
            ImageUrl = product.ImageUrl,
            Category = product.Category,
            IsActive = product.IsActive,
            CreatedAt = product.CreatedAt
        };
    }
}
