using OrderPaymentMicroservice.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Services.Interfaces;

namespace OrderPaymentMicroservice.Services.Payments;

/// <summary>
/// Reconciles durable in-flight gateway operations after timeout or process
/// restart. It never initiates a new charge; retry policy remains explicit.
/// </summary>
public sealed class PaymentRecoveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentRecoveryWorker> _logger;
    private readonly PaymentRecoveryOptions _options;
    private readonly IAuditWriter? _audit;

    public PaymentRecoveryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<PaymentRecoveryOptions> options,
        ILogger<PaymentRecoveryWorker> logger,
        IAuditWriter? audit = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _audit = audit;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Payment recovery worker is disabled");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.ScanIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        await ReconcileBatchSafelyAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ReconcileBatchSafelyAsync(stoppingToken);
    }

    private async Task ReconcileBatchSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileBatchAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment recovery scan failed");
        }
    }

    private async Task ReconcileBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
        var cutoff = DateTime.UtcNow.AddSeconds(-Math.Max(5, _options.StaleAfterSeconds));
        var batchSize = Math.Clamp(_options.BatchSize, 1, 500);

        var candidates = await context.Payments
            .AsNoTracking()
            .Where(payment => payment.UpdatedAt <= cutoff &&
                (payment.Status == PaymentStatus.PROCESSING ||
                 payment.Status == PaymentStatus.UNKNOWN ||
                 payment.Status == PaymentStatus.REFUND_PENDING))
            .OrderBy(payment => payment.UpdatedAt)
            .Select(payment => new { payment.Id, payment.Status })
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ChangeTracker.Clear();
            try
            {
                var correlation = Guid.NewGuid();
                AuditEvent Entry(string phase, string outcome) => new()
                {
                    CorrelationId = correlation, ActorKind = "SYSTEM", ResourceType = "PAYMENT", ResourceId = candidate.Id,
                    Action = candidate.Status == PaymentStatus.REFUND_PENDING ? "REFUND_RECONCILE" : "PAYMENT_RECONCILE", Phase = phase, Outcome = outcome
                };
                if (_audit != null) await _audit.AppendAsync(Entry("BEGIN", "STARTED"));
                var result = candidate.Status == PaymentStatus.REFUND_PENDING
                    ? await service.ReconcileRefundAsync(candidate.Id, cancellationToken)
                    : await service.ReconcilePaymentAsync(candidate.Id, cancellationToken);
                if (_audit != null) await _audit.AppendAsync(Entry("RESULT", result.Status));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogDebug("Payment {PaymentId} changed during recovery; it will be re-evaluated later", candidate.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not reconcile payment {PaymentId} in state {PaymentStatus}", candidate.Id, candidate.Status);
            }
        }
    }
}
