using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Operations;

namespace OrderPaymentMicroservice.Auditing;

public interface IAuditWriter { Task AppendAsync(AuditEvent entry); }

public sealed class AuditWriter(IServiceScopeFactory scopes, OperationalTelemetry? telemetry = null) : IAuditWriter
{
    public async Task AppendAsync(AuditEvent entry)
    {
        // Never SaveChanges on the business context: failed entity mutations must not leak into audit writes.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        db.AuditEvents.Add(entry);
        try { await db.SaveChangesAsync(budget.Token); }
        catch { telemetry?.AuditFailure(); throw; }
    }
}
