using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Products;
using OrderPaymentMicroservice.DTOs.Users;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public class RequestBoundaryRegressionTests
{
    [Theory]
    [InlineData(6, false)]
    [InlineData(5, true)]
    public async Task DuplicateProductLinesRespectCombinedStock(int perLine, bool accepted)
    {
        await using var db = CreateContext();
        var product = await SeedProduct(db);
        var service = new OrderService(db, new InputValidator());
        var request = new CreateOrderRequest
        {
            CustomerId = 1, TotalAmount = perLine * 2 * product.Price,
            Items = [new() { ProductId = product.Id, Quantity = perLine, UnitPrice = product.Price },
                     new() { ProductId = product.Id, Quantity = perLine, UnitPrice = product.Price }]
        };
        if (accepted)
        {
            var order = await service.CreateOrderAsync(request);
            Assert.Equal(0, product.Stock);
            Assert.True(await service.CancelOrderAsync(order.Id));
            Assert.Equal(10, product.Stock);
            Assert.False(await service.CancelOrderAsync(order.Id));
            Assert.Equal(10, product.Stock);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(request));
            Assert.Equal(10, product.Stock);
            Assert.Empty(db.Orders);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(10001)]
    [InlineData(int.MaxValue)]
    public async Task InvalidCartAdditionDoesNotCreateCartOrItems(int quantity)
    {
        await using var db = CreateContext();
        var product = await SeedProduct(db);
        var service = Cart(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddItemToCartAsync(1, product.Id, quantity));
        Assert.Empty(db.CartItems);
        Assert.Empty(db.ShoppingCarts);
    }

    [Fact]
    public async Task CartAdditionChecksCombinedQuantityAndLeavesTotalUnchangedOnRejection()
    {
        await using var db = CreateContext();
        var product = await SeedProduct(db);
        var service = Cart(db);
        var cart = await service.AddItemToCartAsync(1, product.Id, 6);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddItemToCartAsync(1, product.Id, 5));
        var persisted = await service.GetCartByIdAsync(cart.Id);
        Assert.Equal(6, Assert.Single(persisted!.Items).Quantity);
        Assert.Equal(150m, persisted.TotalPrice);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(10001)]
    public async Task InvalidCartUpdatePreservesItemAndTotal(int quantity)
    {
        await using var db = CreateContext();
        var product = await SeedProduct(db);
        var service = Cart(db);
        var cart = await service.AddItemToCartAsync(1, product.Id, 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateCartItemAsync(cart.Id, cart.Items.Single().Id, quantity));
        var persisted = await service.GetCartByIdAsync(cart.Id);
        Assert.Equal(2, persisted!.Items.Single().Quantity);
        Assert.Equal(50m, persisted.TotalPrice);
    }

    [Fact]
    public async Task CartZeroUpdateRemovesItemAndRecalculatesTotal()
    {
        await using var db = CreateContext();
        var product = await SeedProduct(db);
        var service = Cart(db);
        var cart = await service.AddItemToCartAsync(1, product.Id, 2);
        var result = await service.UpdateCartItemAsync(cart.Id, cart.Items.Single().Id, 0);
        Assert.Empty(result.Items);
        Assert.Equal(0m, result.TotalPrice);
    }

    [Fact]
    public async Task InactiveProductCannotBeAddedOrUpdatedInCart()
    {
        await using var db = CreateContext();
        var product = await SeedProduct(db);
        var service = Cart(db);
        var cart = await service.AddItemToCartAsync(1, product.Id, 2);
        product.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddItemToCartAsync(1, product.Id, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateCartItemAsync(cart.Id, cart.Items.Single().Id, 3));
        Assert.Equal(50m, (await service.GetCartByIdAsync(cart.Id))!.TotalPrice);
    }

    [Theory]
    [InlineData("invalid", "StrongPassword!123", "Customer")]
    [InlineData("", "StrongPassword!123", "Customer")]
    [InlineData("customer@example.test", "", "Customer")]
    [InlineData("customer@example.test", "short", "Customer")]
    [InlineData("customer@example.test", "StrongPassword!123", " ")]
    public async Task InvalidRegistrationDoesNotPersistUser(string email, string password, string name)
    {
        await using var db = CreateContext();
        var service = new UserService(db, NullLogger<UserService>.Instance, new PasswordHasher<User>(), new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterAsync(new RegisterRequest
        { Email = email, Password = password, FullName = name }));
        Assert.Empty(db.Users);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 10)]
    [InlineData(25, -1)]
    [InlineData(1000000, 10)]
    public async Task InvalidProductCreateAndUpdateDoNotPersistChanges(int price, int stock)
    {
        await using var db = CreateContext();
        var product = await SeedProduct(db);
        var service = new ProductService(db, NullLogger<ProductService>.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(new CreateProductRequest
        { Name = "Product", Price = price, Stock = stock }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateProductAsync(product.Id, new UpdateProductRequest
        { Price = price, Stock = stock }));
        Assert.Single(db.Products);
        Assert.Equal(25m, product.Price);
        Assert.Equal(10, product.Stock);
    }

    private static CartService Cart(AppDbContext db) => new(db, NullLogger<CartService>.Instance);
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<Product> SeedProduct(AppDbContext db)
    {
        var product = new Product { Name = "Product", Price = 25m, Stock = 10, IsActive = true };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }
}
