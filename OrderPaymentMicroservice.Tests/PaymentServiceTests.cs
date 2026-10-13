using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services;
using OrderPaymentMicroservice.Services.Payments;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public class PaymentServiceTests
{
    [Fact]
    public async Task CancelledOrderCannotBePaid()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.CANCELLED, 100m);
        var service = CreateService(context, GatewayOutcome.Succeeded);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePaymentAsync(Request(order, "cancelled-attempt")));

        Assert.Contains("cannot be paid", exception.Message);
        Assert.Empty(context.Payments);
        Assert.Equal(OrderStatus.CANCELLED, order.Status);
    }

    [Fact]
    public async Task PaymentAmountMustEqualOrderTotal()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.PAYMENT_PENDING, 100m);
        var service = CreateService(context, GatewayOutcome.Succeeded);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePaymentAsync(new CreatePaymentRequest
        {
            OrderId = order.Id,
            Amount = 99m,
            IdempotencyKey = "wrong-amount"
        }));

        Assert.Empty(context.Payments);
        Assert.Equal(OrderStatus.PAYMENT_PENDING, order.Status);
    }

    [Fact]
    public async Task SuccessfulGatewayResultMovesPaymentAndOrderAtomically()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.PAYMENT_PENDING, 100m);
        var service = CreateService(context, GatewayOutcome.Succeeded);

        var payment = await service.CreatePaymentAsync(Request(order, "successful-attempt"));

        Assert.Equal(PaymentStatus.SUCCESS.ToString(), payment.Status);
        Assert.Equal(OrderStatus.PAID, order.Status);
        Assert.Equal(1, payment.AttemptCount);
        Assert.Single(context.Payments);
    }

    [Fact]
    public async Task RetryingSameIdempotencyKeyReturnsOriginalPayment()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.PAYMENT_PENDING, 100m);
        var gateway = new ScriptedGateway(new GatewayResult(GatewayOutcome.Succeeded, "TX-1"));
        var service = new PaymentService(context, new PaymentValidator(), gateway);
        var request = Request(order, "stable-client-request-id");

        var first = await service.CreatePaymentAsync(request);
        var retry = await service.CreatePaymentAsync(request);

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(first.TransactionCode, retry.TransactionCode);
        Assert.Single(context.Payments);
        Assert.Equal(1, gateway.ChargeCalls);
    }

    [Fact]
    public async Task TimeoutBecomesUnknownAndRetryUsesSameLogicalPayment()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.PAYMENT_PENDING, 100m);
        var gateway = new ScriptedGateway(
            new GatewayResult(GatewayOutcome.Unknown, null, "TIMEOUT", "No final response"),
            new GatewayResult(GatewayOutcome.Succeeded, "TX-RECOVERED"));
        var service = new PaymentService(context, new PaymentValidator(), gateway);

        var unknown = await service.CreatePaymentAsync(Request(order, "timeout-attempt"));
        var recovered = await service.RetryPaymentAsync(unknown.Id);

        Assert.Equal(PaymentStatus.UNKNOWN.ToString(), unknown.Status);
        Assert.Equal(PaymentStatus.SUCCESS.ToString(), recovered.Status);
        Assert.Equal(2, recovered.AttemptCount);
        Assert.Equal("TX-RECOVERED", recovered.TransactionCode);
        Assert.Equal(OrderStatus.PAID, order.Status);
        Assert.Single(context.Payments);
        Assert.Equal(2, gateway.ChargeCalls);
    }

    [Fact]
    public async Task ExhaustedUnknownPaymentRemainsReservedUntilOutcomeIsKnown()
    {
        await using var context = CreateContext();
        var (order, product) = await AddOrderWithReservedProduct(context, stockAfterReservation: 8, quantity: 2);
        var gateway = new ScriptedGateway(
            new GatewayResult(GatewayOutcome.Unknown, null, "TIMEOUT"),
            new GatewayResult(GatewayOutcome.Unknown, null, "TIMEOUT"));
        var service = new PaymentService(context, new PaymentValidator(), gateway);

        var first = await service.CreatePaymentAsync(Request(order, "bounded-retry"));
        var second = await service.RetryPaymentAsync(first.Id);
        var exhausted = await service.RetryPaymentAsync(second.Id);

        Assert.Equal(PaymentStatus.UNKNOWN.ToString(), exhausted.Status);
        Assert.Equal("RETRY_EXHAUSTED_RECONCILIATION_REQUIRED", exhausted.LastErrorCode);
        Assert.Equal(OrderStatus.PAYMENT_PENDING, order.Status);
        Assert.Equal(InventoryReservationStatus.RESERVED, order.InventoryStatus);
        Assert.Equal(8, product.Stock);
        Assert.Equal(2, gateway.ChargeCalls);
        Assert.False(await new OrderService(context, new InputValidator()).CancelOrderAsync(order.Id));
        Assert.Equal(8, product.Stock);
        Assert.Equal(InventoryReservationStatus.RESERVED, order.InventoryStatus);
    }

    [Fact]
    public async Task DeclineFailsOrderAndReleasesInventory()
    {
        await using var context = CreateContext();
        var (order, product) = await AddOrderWithReservedProduct(context, 8, 2);
        var service = CreateService(context, GatewayOutcome.Declined);

        var payment = await service.CreatePaymentAsync(Request(order, "declined"));

        Assert.Equal(PaymentStatus.FAILED.ToString(), payment.Status);
        Assert.Equal(OrderStatus.FAILED, order.Status);
        Assert.Equal(InventoryReservationStatus.RELEASED, order.InventoryStatus);
        Assert.Equal(10, product.Stock);
    }

    [Fact]
    public async Task SuccessfulRefundCompensatesOrderAndReleasesInventory()
    {
        await using var context = CreateContext();
        var (order, product) = await AddOrderWithReservedProduct(context, 8, 2);
        var gateway = new ScriptedGateway(
            new GatewayResult(GatewayOutcome.Succeeded, "TX-PAID"),
            refundResults: [new GatewayResult(GatewayOutcome.Succeeded, "RF-1")]);
        var service = new PaymentService(context, new PaymentValidator(), gateway);
        var paid = await service.CreatePaymentAsync(Request(order, "charge-key"));

        var refunded = await service.RefundPaymentAsync(paid.Id, "refund-key");

        Assert.Equal(PaymentStatus.REFUNDED.ToString(), refunded.Status);
        Assert.Equal("RF-1", refunded.RefundTransactionCode);
        Assert.Equal(OrderStatus.REFUNDED, order.Status);
        Assert.Equal(InventoryReservationStatus.RELEASED, order.InventoryStatus);
        Assert.Equal(10, product.Stock);
    }

    [Fact]
    public async Task UnknownRefundStaysPendingUntilSameRefundIsRetried()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.PAYMENT_PENDING, 100m);
        var gateway = new ScriptedGateway(
            new GatewayResult(GatewayOutcome.Succeeded, "TX-PAID"),
            refundResults:
            [
                new GatewayResult(GatewayOutcome.Unknown, "TX-PAID", "REFUND_TIMEOUT"),
                new GatewayResult(GatewayOutcome.Succeeded, "RF-RECOVERED")
            ]);
        var service = new PaymentService(context, new PaymentValidator(), gateway);
        var paid = await service.CreatePaymentAsync(Request(order, "charge-key"));

        var pending = await service.RefundPaymentAsync(paid.Id, "refund-key");
        var refunded = await service.RetryRefundAsync(paid.Id);

        Assert.Equal(PaymentStatus.REFUND_PENDING.ToString(), pending.Status);
        Assert.Equal(PaymentStatus.REFUNDED.ToString(), refunded.Status);
        Assert.Equal(OrderStatus.REFUNDED, order.Status);
        Assert.Equal(2, gateway.RefundCalls);
    }

    [Fact]
    public async Task RefundReconciliationRecoversAfterProcessCrashWithoutIssuingAnotherRefund()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.PAYMENT_PENDING, 100m);
        var gateway = new ScriptedGateway(
            new GatewayResult(GatewayOutcome.Succeeded, "TX-PAID"),
            refundResults: [new GatewayResult(GatewayOutcome.Unknown, ErrorCode: "REFUND_TIMEOUT")]);
        var service = new PaymentService(context, new PaymentValidator(), gateway);
        var paid = await service.CreatePaymentAsync(Request(order, "charge-before-refund-crash"));
        var pending = await service.RefundPaymentAsync(paid.Id, "stable-refund-key");
        gateway.RefundStatusResult = new GatewayResult(GatewayOutcome.Succeeded, "RF-FOUND");

        var reconciled = await service.ReconcileRefundAsync(pending.Id);

        Assert.Equal(PaymentStatus.REFUNDED.ToString(), reconciled.Status);
        Assert.Equal("RF-FOUND", reconciled.RefundTransactionCode);
        Assert.Equal(OrderStatus.REFUNDED, order.Status);
        Assert.Equal(1, gateway.RefundCalls);
    }

    [Fact]
    public async Task ReconciliationRecoversPaymentLeftProcessingByProcessCrash()
    {
        await using var context = CreateContext();
        var order = await AddOrder(context, OrderStatus.PAYMENT_PENDING, 100m);
        var payment = new Payment
        {
            OrderId = order.Id,
            Amount = 100m,
            Status = PaymentStatus.PROCESSING,
            IdempotencyKey = "crash-window",
            AttemptCount = 1
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        var gateway = new ScriptedGateway(new GatewayResult(GatewayOutcome.Unknown))
        {
            ChargeStatusResult = new GatewayResult(GatewayOutcome.Succeeded, "TX-FOUND")
        };
        var service = new PaymentService(context, new PaymentValidator(), gateway);

        var reconciled = await service.ReconcilePaymentAsync(payment.Id);

        Assert.Equal(PaymentStatus.SUCCESS.ToString(), reconciled.Status);
        Assert.Equal("TX-FOUND", reconciled.TransactionCode);
        Assert.Equal(OrderStatus.PAID, order.Status);
        Assert.Equal(0, gateway.ChargeCalls);
    }

    [Fact]
    public async Task CancellationCannotWinAfterPaymentEnteredProcessing()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        int orderId;
        await using (var seed = new AppDbContext(options))
            orderId = (await AddOrder(seed, OrderStatus.PAYMENT_PENDING, 100m)).Id;

        await using var paymentContext = new AppDbContext(options);
        await using var cancellationContext = new AppDbContext(options);
        var gateway = new BlockingGateway();
        var paymentService = new PaymentService(paymentContext, new PaymentValidator(), gateway);
        var cancellationService = new OrderService(cancellationContext, new InputValidator());

        var paymentTask = paymentService.CreatePaymentAsync(new CreatePaymentRequest
        {
            OrderId = orderId,
            Amount = 100m,
            IdempotencyKey = "payment-wins-lock"
        });
        await gateway.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cancelled = await cancellationService.CancelOrderAsync(orderId);
        gateway.Release.TrySetResult();
        var payment = await paymentTask;

        Assert.False(cancelled);
        Assert.Equal(PaymentStatus.SUCCESS.ToString(), payment.Status);
        await using var verification = new AppDbContext(options);
        Assert.Equal(OrderStatus.PAID, (await verification.Orders.SingleAsync()).Status);
    }

    private static PaymentService CreateService(AppDbContext context, GatewayOutcome outcome) =>
        new(context, new PaymentValidator(), new ScriptedGateway(new GatewayResult(outcome, "TX-TEST", "DECLINED")));

    private static CreatePaymentRequest Request(Order order, string key) => new()
    {
        OrderId = order.Id,
        Amount = order.TotalAmount,
        IdempotencyKey = key
    };

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
            TotalAmount = total,
            InventoryStatus = status == OrderStatus.CANCELLED
                ? InventoryReservationStatus.RELEASED
                : InventoryReservationStatus.RESERVED
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }

    private static async Task<(Order Order, Product Product)> AddOrderWithReservedProduct(
        AppDbContext context,
        int stockAfterReservation,
        int quantity)
    {
        var product = new Product
        {
            Name = "Reserved product",
            Price = 50m,
            Stock = stockAfterReservation,
            IsActive = true
        };
        var order = new Order
        {
            CustomerId = 1,
            UserId = 1,
            Status = OrderStatus.PAYMENT_PENDING,
            TotalAmount = 100m,
            InventoryStatus = InventoryReservationStatus.RESERVED,
            OrderItems = [new OrderItem { Product = product, Quantity = quantity, UnitPrice = 50m, SubTotal = 100m }]
        };
        context.Add(order);
        await context.SaveChangesAsync();
        return (order, product);
    }

    private sealed class ScriptedGateway : IPaymentGateway
    {
        private readonly Queue<GatewayResult> _chargeResults;
        private readonly Queue<GatewayResult> _refundResults;

        public ScriptedGateway(GatewayResult chargeResult, IEnumerable<GatewayResult>? refundResults = null)
            : this([chargeResult], refundResults)
        {
        }

        public ScriptedGateway(GatewayResult firstChargeResult, GatewayResult secondChargeResult)
            : this([firstChargeResult, secondChargeResult], null)
        {
        }

        public ScriptedGateway(IEnumerable<GatewayResult> chargeResults, IEnumerable<GatewayResult>? refundResults = null)
        {
            _chargeResults = new Queue<GatewayResult>(chargeResults);
            _refundResults = new Queue<GatewayResult>(refundResults ?? []);
        }

        public int ChargeCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public GatewayResult ChargeStatusResult { get; set; } = new(GatewayOutcome.Unknown, ErrorCode: "NOT_FOUND");
        public GatewayResult RefundStatusResult { get; set; } = new(GatewayOutcome.Unknown, ErrorCode: "NOT_FOUND");

        public Task<GatewayResult> ChargeAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
        {
            ChargeCalls++;
            return Task.FromResult(_chargeResults.Dequeue());
        }

        public Task<GatewayResult> RefundAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            return Task.FromResult(_refundResults.Dequeue());
        }

        public Task<GatewayResult> GetChargeStatusAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ChargeStatusResult);

        public Task<GatewayResult> GetRefundStatusAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(RefundStatusResult);
    }

    private sealed class BlockingGateway : IPaymentGateway
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GatewayResult> ChargeAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new GatewayResult(GatewayOutcome.Succeeded, "TX-BLOCKING");
        }

        public Task<GatewayResult> RefundAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GatewayResult(GatewayOutcome.Succeeded, "RF-BLOCKING"));

        public Task<GatewayResult> GetChargeStatusAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GatewayResult(GatewayOutcome.Unknown));

        public Task<GatewayResult> GetRefundStatusAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GatewayResult(GatewayOutcome.Unknown));
    }
}
