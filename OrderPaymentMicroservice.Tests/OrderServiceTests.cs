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

    [Fact]
    public async Task CancellingPendingOrderReleasesReservedInventoryExactlyOnce()
    {
        await using var context = CreateContext();
        var product = await AddProduct(context, price: 25m, stock: 10);
        var service = new OrderService(context, new InputValidator());
        var created = await service.CreateOrderAsync(new CreateOrderRequest
        {
            CustomerId = 1,
            TotalAmount = 50m,
            Items = [new() { ProductId = product.Id, Quantity = 2, UnitPrice = 25m }]
        });

        var firstCancel = await service.CancelOrderAsync(created.Id);
        var secondCancel = await service.CancelOrderAsync(created.Id);

        Assert.True(firstCancel);
        Assert.False(secondCancel);
        Assert.Equal(10, product.Stock);
        var order = await context.Orders.FindAsync(created.Id);
        Assert.Equal(OrderStatus.CANCELLED, order!.Status);
        Assert.Equal(InventoryReservationStatus.RELEASED, order.InventoryStatus);
    }

    [Fact]
    public async Task PaidOrderMustBeConfirmedBeforeCompletion()
    {
        await using var context = CreateContext();
        var order = new Order
        {
            CustomerId = 1,
            UserId = 1,
            TotalAmount = 100m,
            Status = OrderStatus.PAID,
            InventoryStatus = InventoryReservationStatus.RESERVED,
            Payments = [new Payment { Amount = 100m, Status = PaymentStatus.SUCCESS, IdempotencyKey = "paid" }]
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var service = new OrderService(context, new InputValidator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteOrderAsync(order.Id));
        var confirmed = await service.ConfirmOrderAsync(order.Id);
        var completed = await service.CompleteOrderAsync(order.Id);

        Assert.Equal(OrderStatus.CONFIRMED.ToString(), confirmed.Status);
        Assert.Equal(OrderStatus.COMPLETED.ToString(), completed.Status);
        Assert.Equal(InventoryReservationStatus.CONSUMED.ToString(), completed.InventoryStatus);
    }

    [Fact]
    public async Task OrderWithoutSuccessfulPaymentCannotBeConfirmed()
    {
        await using var context = CreateContext();
        var order = new Order
        {
            CustomerId = 1,
            UserId = 1,
            TotalAmount = 100m,
            Status = OrderStatus.PAID,
            InventoryStatus = InventoryReservationStatus.RESERVED
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var service = new OrderService(context, new InputValidator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmOrderAsync(order.Id));
        Assert.Equal(OrderStatus.PAID, order.Status);
    }

    [Fact]
    public async Task ConcurrentOrdersCannotBothUseTheSameStaleStockVersion()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        await using (var seed = new AppDbContext(options))
            await AddProduct(seed, price: 10m, stock: 10);

        await using var firstContext = new AppDbContext(options);
        await using var secondContext = new AppDbContext(options);
        var firstProduct = await firstContext.Products.SingleAsync();
        var staleProduct = await secondContext.Products.SingleAsync();
        var firstService = new OrderService(firstContext, new InputValidator());
        var secondService = new OrderService(secondContext, new InputValidator());

        await firstService.CreateOrderAsync(new CreateOrderRequest
        {
            CustomerId = 1,
            TotalAmount = 60m,
            Items = [new() { ProductId = firstProduct.Id, Quantity = 6, UnitPrice = 10m }]
        });

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondService.CreateOrderAsync(new CreateOrderRequest
        {
            CustomerId = 2,
            TotalAmount = 60m,
            Items = [new() { ProductId = staleProduct.Id, Quantity = 6, UnitPrice = 10m }]
        }));

        await using var verification = new AppDbContext(options);
        Assert.Equal(4, (await verification.Products.SingleAsync()).Stock);
        // EF InMemory is not transactional and may retain the separately-added
        // Order after a concurrency exception. PostgreSQL integration tests must
        // assert rollback of the whole transaction.
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
