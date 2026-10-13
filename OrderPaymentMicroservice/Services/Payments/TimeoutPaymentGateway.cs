using System.ComponentModel.DataAnnotations;

namespace OrderPaymentMicroservice.Services.Payments;

public sealed class GatewayCallTimeoutOptions
{
    [Range(50, 60000)]
    public int RequestTimeoutMilliseconds { get; set; } = 5000;
}

/// <summary>Bounds caller waiting even if a provider ignores cancellation. A timed-out operation may still settle externally.</summary>
public sealed class TimeoutPaymentGateway : IPaymentGateway
{
    private readonly IPaymentGateway _inner;
    private readonly TimeSpan _timeout;
    public TimeoutPaymentGateway(IPaymentGateway inner, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(timeout));
        _inner = inner; _timeout = timeout;
    }
    public Task<GatewayResult> ChargeAsync(GatewayPaymentRequest r, CancellationToken t = default) => Call(ct => _inner.ChargeAsync(r, ct), t);
    public Task<GatewayResult> RefundAsync(GatewayPaymentRequest r, CancellationToken t = default) => Call(ct => _inner.RefundAsync(r, ct), t);
    public Task<GatewayResult> GetChargeStatusAsync(GatewayPaymentRequest r, CancellationToken t = default) => Call(ct => _inner.GetChargeStatusAsync(r, ct), t);
    public Task<GatewayResult> GetRefundStatusAsync(GatewayPaymentRequest r, CancellationToken t = default) => Call(ct => _inner.GetRefundStatusAsync(r, ct), t);

    private async Task<GatewayResult> Call(Func<CancellationToken, Task<GatewayResult>> operation, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(caller);
        budget.CancelAfter(_timeout);
        var task = operation(budget.Token);
        // Observe a late fault from a non-cooperative adapter; never apply its late result here.
        _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try { return await task.WaitAsync(budget.Token); }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested && budget.IsCancellationRequested)
        { throw new TimeoutException("Gateway response deadline exceeded; outcome requires reconciliation"); }
    }
}
