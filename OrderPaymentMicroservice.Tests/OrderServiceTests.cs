using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public class OrderServiceTests
{
    [Fact]
    public async Task OrderUsesServerPriceAndDecrementsStock()
    {
        await using var context = CreateContext();
        var product = await AddProduct(context, price: 25m, stock: 10);
        var service = new OrderService(context, new InputValidator());

        var response = await service.CreateOrderAsync(new CreateOrderRequest
        {
            CustomerId = 1,
            TotalAmount = 50m,
            Items = [new() { ProductId = product.Id, Quantity = 2, UnitPrice = 25m }]
        });

        Assert.Equal(50m, response.TotalAmount);
        Assert.Equal(25m, response.Items.Single().UnitPrice);
        Assert.Equal(8, product.Stock);
        Assert.Equal(OrderStatus.PAYMENT_PENDING.ToString(), response.Status);
    }

    [Fact]
    public async Task ClientCannotCreateOrderUsingTamperedPrice()
    {
        await using var context = CreateContext();
        var product = await AddProduct(context, price: 25m, stock: 10);
        var service = new OrderService(context, new InputValidator());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateOrderAsync(new CreateOrderRequest
        {
            CustomerId = 1,
            TotalAmount = 2m,
            Items = [new() { ProductId = product.Id, Quantity = 2, UnitPrice = 1m }]
        }));

        Assert.Empty(context.Orders);
        Assert.Equal(10, product.Stock);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Product> AddProduct(AppDbContext context, decimal price, int stock)
    {
        var product = new Product
        {
            Name = "Test product",
            Description = "Test",
            Price = price,
            Stock = stock,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }
}
