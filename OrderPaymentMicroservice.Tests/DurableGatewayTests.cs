using OrderPaymentMicroservice.Services.Payments;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public sealed class DurableGatewayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gateway-test-" + Guid.NewGuid().ToString("N"));
    private static GatewayPaymentRequest Request => new(1, 2, 25m, "../../same-key");

    [Fact]
    public async Task RestartRetainsChargeAndRefundAndBindsPayload()
    {
        var first = new DurableFakePaymentGateway(_root);
        var charge = await first.ChargeAsync(Request);
        var refundRequest = Request with { IdempotencyKey = "refund-key", ExistingTransactionCode = charge.TransactionCode };
        var refund = await first.RefundAsync(refundRequest);
        var restarted = new DurableFakePaymentGateway(_root);
        Assert.Equal(charge, await restarted.GetChargeStatusAsync(Request));
        Assert.Equal(charge, await restarted.ChargeAsync(Request));
        Assert.Equal(refund, await restarted.GetRefundStatusAsync(refundRequest));
        Assert.Equal(refund, await restarted.RefundAsync(refundRequest));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ChargeAsync(Request with { Amount = 24m }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ChargeAsync(Request with { OrderId = 3 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.RefundAsync(refundRequest with { ExistingTransactionCode = "different-charge" }));
        Assert.Equal(2, Directory.GetFiles(_root, "*.json").Length);
    }

    [Fact]
    public async Task ConcurrentGatewayInstancesPublishOneCharge()
    {
        var gateways = Enumerable.Range(0, 20).Select(_ => new DurableFakePaymentGateway(_root)).ToArray();
        var results = await Task.WhenAll(gateways.Select(g => g.ChargeAsync(Request)));
        Assert.Single(results.Select(r => r.TransactionCode).Distinct());
        Assert.Single(Directory.GetFiles(_root, "*.json"));
    }

    [Fact]
    public async Task MissingOutcomeAndCancelledCallDoNotPublishCharge()
    {
        var gateway = new DurableFakePaymentGateway(_root);
        Assert.Equal(GatewayOutcome.Unknown, (await gateway.GetChargeStatusAsync(Request)).Outcome);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.ChargeAsync(Request, cancellation.Token));
        Assert.Empty(Directory.GetFiles(_root, "*.json"));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
