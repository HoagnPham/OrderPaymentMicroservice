using System.ComponentModel.DataAnnotations;
using OrderPaymentMicroservice.Security;

namespace OrderPaymentMicroservice.DTOs.Products;

public class CreateProductRequest
{
    [Required, StringLength(255)]
    public string Name { get; set; } = string.Empty;
    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;
    [MoneyAmount]
    [Range(typeof(decimal), "0.01", "999999.99")]
    public decimal Price { get; set; }
    [Range(0, int.MaxValue)]
    public int Stock { get; set; }
    [StringLength(500)]
    public string? ImageUrl { get; set; }
    [StringLength(100)]
    public string? Category { get; set; }
}

public class UpdateProductRequest
{
    [StringLength(255)]
    public string? Name { get; set; }
    [StringLength(1000)]
    public string? Description { get; set; }
    [MoneyAmount]
    [Range(typeof(decimal), "0.01", "999999.99")]
    public decimal? Price { get; set; }
    [Range(0, int.MaxValue)]
    public int? Stock { get; set; }
    [StringLength(500)]
    public string? ImageUrl { get; set; }
    [StringLength(100)]
    public string? Category { get; set; }
}

public class ProductResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    public string? Category { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
