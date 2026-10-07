using System.Collections.Concurrent;

namespace OrderPaymentMicroservice.Services.Payments;

/// <summary>
/// Deterministic, process-local gateway for development. Results are retained
/// by idempotency key so reconciliation behaves like an external gateway.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, GatewayResult> _charges = new();
    private readonly ConcurrentDictionary<string, GatewayResult> _refunds = new();

    public Task<GatewayResult> ChargeAsync(
        GatewayPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = _charges.GetOrAdd(request.IdempotencyKey, _ => new GatewayResult(
            GatewayOutcome.Succeeded,
            $"FAKE-{Guid.NewGuid():N}"));
        return Task.FromResult(result);
    }

    public Task<GatewayResult> RefundAsync(
        GatewayPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = _refunds.GetOrAdd(request.IdempotencyKey, _ => new GatewayResult(
            GatewayOutcome.Succeeded,
            $"FAKE-REFUND-{Guid.NewGuid():N}"));
        return Task.FromResult(result);
    }

    public Task<GatewayResult> GetChargeStatusAsync(
        GatewayPaymentRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_charges.TryGetValue(request.IdempotencyKey, out var result)
            ? result
            : new GatewayResult(GatewayOutcome.Unknown, ErrorCode: "NOT_FOUND"));

    public Task<GatewayResult> GetRefundStatusAsync(
        GatewayPaymentRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_refunds.TryGetValue(request.IdempotencyKey, out var result)
            ? result
            : new GatewayResult(GatewayOutcome.Unknown, ErrorCode: "NOT_FOUND"));
}
