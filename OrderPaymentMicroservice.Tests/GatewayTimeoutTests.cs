using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services;
using OrderPaymentMicroservice.Services.Interfaces;
using OrderPaymentMicroservice.Services.Payments;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public sealed class GatewayTimeoutTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(75);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task AllGatewayOperationsBoundWaitingForNonCooperativeAdapter(int operation)
    {
        var inner = new ControlledGateway();
        var gateway = new TimeoutPaymentGateway(inner, Budget);
        var request = new GatewayPaymentRequest(1, 1, 25m, "key");
        var call = operation switch
        {
            0 => gateway.ChargeAsync(request), 1 => gateway.RefundAsync(request),
            2 => gateway.GetChargeStatusAsync(request), _ => gateway.GetRefundStatusAsync(request)
        };
        await Assert.ThrowsAsync<TimeoutException>(() => call.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(inner.LastToken.IsCancellationRequested);
        inner.Outcome.SetResult(new GatewayResult(GatewayOutcome.Succeeded, "LATE"));
    }

    [Fact]
    public async Task CallerCancellationIsNotMisreportedAsTimeout()
    {
        var inner = new ControlledGateway();
        var gateway = new TimeoutPaymentGateway(inner, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var call = gateway.GetChargeStatusAsync(new(1, 1, 25m, "key"), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(2)));
        inner.Outcome.SetResult(new(GatewayOutcome.Succeeded));
    }

    [Fact]
    public async Task ChargeTimeoutPreservesReservationAndLateResultRequiresReconciliation()
    {
        await using var db = Context();
        var order = await SeedOrder(db);
        var inner = new ControlledGateway();
        var service = new PaymentService(db, new PaymentValidator(), new TimeoutPaymentGateway(inner, Budget));
        var payment = await service.CreatePaymentAsync(new CreatePaymentRequest { OrderId = order.Id, Amount = 50m, IdempotencyKey = "charge-timeout" });
        Assert.Equal("UNKNOWN", payment.Status);
        Assert.Equal("GATEWAY_TIMEOUT", payment.LastErrorCode);
        Assert.Equal(OrderStatus.PAYMENT_PENDING, order.Status);
        Assert.Equal(InventoryReservationStatus.RESERVED, order.InventoryStatus);
        Assert.Equal(8, (await db.Products.SingleAsync()).Stock);
        inner.Outcome.SetResult(new(GatewayOutcome.Succeeded, "LATE-TX"));
        Assert.Equal(PaymentStatus.UNKNOWN, (await db.Payments.SingleAsync()).Status);
        var reconciled = await service.ReconcilePaymentAsync(payment.Id);
        Assert.Equal("SUCCESS", reconciled.Status);
        Assert.Equal("LATE-TX", reconciled.TransactionCode);
        Assert.Equal(1, reconciled.AttemptCount);
        Assert.Equal(1, inner.ChargeCalls);
        Assert.Equal(8, (await db.Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task RefundTimeoutKeepsCompensationPendingUntilStatusIsKnown()
    {
        await using var db = Context();
        var order = await SeedOrder(db);
        order.Status = OrderStatus.PAID;
        var payment = new Payment { Order = order, Amount = 50m, Status = PaymentStatus.SUCCESS, IdempotencyKey = "paid", TransactionCode = "CHARGE" };
        db.Payments.Add(payment); await db.SaveChangesAsync();
        var inner = new ControlledGateway();
        var service = new PaymentService(db, new PaymentValidator(), new TimeoutPaymentGateway(inner, Budget));
        var pending = await service.RefundPaymentAsync(payment.Id, "refund-key");
        Assert.Equal("REFUND_PENDING", pending.Status);
        Assert.Equal("REFUND_GATEWAY_TIMEOUT", pending.LastErrorCode);
        Assert.Equal(8, (await db.Products.SingleAsync()).Stock);
        inner.Outcome.SetResult(new(GatewayOutcome.Succeeded, "REFUND"));
        Assert.Equal("REFUNDED", (await service.ReconcileRefundAsync(payment.Id)).Status);
        Assert.Equal(10, (await db.Products.SingleAsync()).Stock);
        await service.ReconcileRefundAsync(payment.Id);
        Assert.Equal(10, (await db.Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task WorkerContinuesAfterHungStatusLookupAndStopsPromptly()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        int hangingId, readyId;
        await using (var db = new AppDbContext(options))
        {
            var first = await SeedOrder(db); var second = await SeedOrder(db);
            var a = new Payment { Order = first, Amount = 50m, IdempotencyKey = "hang", Status = PaymentStatus.PROCESSING, UpdatedAt = DateTime.UtcNow.AddMinutes(-5) };
            var b = new Payment { Order = second, Amount = 50m, IdempotencyKey = "ready", Status = PaymentStatus.PROCESSING, UpdatedAt = DateTime.UtcNow.AddMinutes(-4) };
            db.Payments.AddRange(a, b); await db.SaveChangesAsync(); hangingId = a.Id; readyId = b.Id;
        }
        var inner = new ControlledGateway { ReadyKey = "ready" };
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(options)); services.AddScoped<PaymentValidator>();
        services.AddSingleton<IPaymentGateway>(new TimeoutPaymentGateway(inner, Budget));
        services.AddScoped<IPaymentService, PaymentService>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new PaymentRecoveryWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PaymentRecoveryOptions()), NullLogger<PaymentRecoveryWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            await using var check = new AppDbContext(options);
            if (await check.Payments.AnyAsync(p => p.Id == readyId && p.Status == PaymentStatus.SUCCESS)) break;
            await Task.Delay(20);
        }
        using var stopBudget = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await worker.StopAsync(stopBudget.Token);
        await using var verification = new AppDbContext(options);
        Assert.Equal(PaymentStatus.SUCCESS, (await verification.Payments.FindAsync(readyId))!.Status);
        Assert.Equal(PaymentStatus.UNKNOWN, (await verification.Payments.FindAsync(hangingId))!.Status);
        Assert.Equal("RECONCILIATION_TIMEOUT", (await verification.Payments.FindAsync(hangingId))!.LastErrorCode);
        Assert.Equal(0, inner.ChargeCalls);
        inner.Outcome.SetResult(new(GatewayOutcome.Succeeded));
    }

    [Fact]
    public async Task WorkerShutdownCancelsActiveLookupWithoutInventingOutcome()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        int paymentId;
        await using (var db = new AppDbContext(options))
        {
            var order = await SeedOrder(db);
            var payment = new Payment { Order = order, Amount = 50m, IdempotencyKey = "shutdown", Status = PaymentStatus.PROCESSING, UpdatedAt = DateTime.UtcNow.AddMinutes(-5) };
            db.Payments.Add(payment); await db.SaveChangesAsync(); paymentId = payment.Id;
        }
        var inner = new ControlledGateway();
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(options)); services.AddScoped<PaymentValidator>();
        services.AddSingleton<IPaymentGateway>(new TimeoutPaymentGateway(inner, TimeSpan.FromSeconds(10)));
        services.AddScoped<IPaymentService, PaymentService>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new PaymentRecoveryWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PaymentRecoveryOptions()), NullLogger<PaymentRecoveryWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await inner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var stopBudget = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await worker.StopAsync(stopBudget.Token);
        Assert.False(stopBudget.IsCancellationRequested);
        Assert.True(inner.LastToken.IsCancellationRequested);
        await using var verification = new AppDbContext(options);
        Assert.Equal(PaymentStatus.PROCESSING, (await verification.Payments.FindAsync(paymentId))!.Status);
        inner.Outcome.SetResult(new(GatewayOutcome.Succeeded));
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<Order> SeedOrder(AppDbContext db)
    {
        var product = new Product { Name = "Reserved", Stock = 8, Price = 25m, IsActive = true };
        db.Products.Add(product);
        var order = new Order { CustomerId = 1, TotalAmount = 50m, Status = OrderStatus.PAYMENT_PENDING, InventoryStatus = InventoryReservationStatus.RESERVED,
            OrderItems = [new() { Product = product, Quantity = 2, UnitPrice = 25m, SubTotal = 50m }] };
        db.Orders.Add(order); await db.SaveChangesAsync(); return order;
    }
    private sealed class ControlledGateway : IPaymentGateway
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<GatewayResult> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken LastToken { get; private set; }
        public int ChargeCalls { get; private set; }
        public string? ReadyKey { get; init; }
        private Task<GatewayResult> Execute(GatewayPaymentRequest r, CancellationToken t)
        { Started.TrySetResult(); LastToken = t; return r.IdempotencyKey == ReadyKey ? Task.FromResult(new GatewayResult(GatewayOutcome.Succeeded, "READY")) : Outcome.Task; }
        public Task<GatewayResult> ChargeAsync(GatewayPaymentRequest r, CancellationToken t = default) { ChargeCalls++; return Execute(r,t); }
        public Task<GatewayResult> RefundAsync(GatewayPaymentRequest r, CancellationToken t = default) => Execute(r,t);
        public Task<GatewayResult> GetChargeStatusAsync(GatewayPaymentRequest r, CancellationToken t = default) => Execute(r,t);
        public Task<GatewayResult> GetRefundStatusAsync(GatewayPaymentRequest r, CancellationToken t = default) => Execute(r,t);
    }
}
