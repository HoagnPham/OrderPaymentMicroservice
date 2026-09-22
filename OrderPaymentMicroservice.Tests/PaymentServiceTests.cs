using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public class PaymentServiceTests
{
    [Fact]
    public async Task CancelledOrderCannotBePaid()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.CANCELLED, 100m);
        var service = new PaymentService(context, new PaymentValidator());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePaymentAsync(new CreatePaymentRequest { OrderId = order.Id, Amount = 100m, IdempotencyKey = "cancelled-attempt" }));

        Assert.Contains("cannot be paid", exception.Message);
        Assert.Empty(context.Payments);
        Assert.Equal(OrderStatus.CANCELLED, order.Status);
    }

    [Fact]
    public async Task PaymentAmountMustEqualOrderTotal()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.PAYMENT_PENDING, 100m);
        var service = new PaymentService(context, new PaymentValidator());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePaymentAsync(new CreatePaymentRequest { OrderId = order.Id, Amount = 99m, IdempotencyKey = "wrong-amount" }));

        Assert.Empty(context.Payments);
        Assert.Equal(OrderStatus.PAYMENT_PENDING, order.Status);
    }

    [Fact]
    public async Task SuccessfulPaymentMovesPendingOrderToPaid()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.PAYMENT_PENDING, 100m);
        var service = new PaymentService(context, new PaymentValidator());

        var payment = await service.CreatePaymentAsync(
            new CreatePaymentRequest { OrderId = order.Id, Amount = 100m, IdempotencyKey = "successful-attempt" });

        Assert.Equal(PaymentStatus.SUCCESS.ToString(), payment.Status);
        Assert.Equal(OrderStatus.PAID, order.Status);
        Assert.Single(context.Payments);
    }

    [Fact]
    public async Task RetryingSameIdempotencyKeyReturnsOriginalPayment()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.PAYMENT_PENDING, 100m);
        var service = new PaymentService(context, new PaymentValidator());
        var request = new CreatePaymentRequest
        {
            OrderId = order.Id,
            Amount = 100m,
            IdempotencyKey = "stable-client-request-id"
        };

        var first = await service.CreatePaymentAsync(request);
        var retry = await service.CreatePaymentAsync(request);

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(first.TransactionCode, retry.TransactionCode);
        Assert.Single(context.Payments);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Order> AddOrder(AppDbContext context, OrderStatus status, decimal total)
    {
        var order = new Order
        {
            CustomerId = 1,
            UserId = 1,
            Status = status,
            TotalAmount = total
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }
}
