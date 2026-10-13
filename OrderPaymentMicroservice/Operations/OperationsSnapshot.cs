using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Services.Payments;

namespace OrderPaymentMicroservice.Operations;

public sealed record OperationsStatus(DateTimeOffset ObservedAt, int PendingCount, int StuckCount, double OldestPendingSeconds,
    int IncompleteAuditCount, bool RecoveryEnabled, string[] Alerts, object Process);

public sealed class OperationsSnapshot(AppDbContext db, IOptions<OperationsOptions> options, IOptions<PaymentRecoveryOptions> recovery,
    OperationalTelemetry telemetry, TimeProvider clock)
{
    public async Task<OperationsStatus> Read(CancellationToken token)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(options.Value.QueryTimeoutMilliseconds);
        var now=clock.GetUtcNow();var cutoff=now.UtcDateTime.AddSeconds(-options.Value.StuckAfterSeconds);
        var pending=db.Payments.AsNoTracking().Where(p=>p.Status==PaymentStatus.PROCESSING||p.Status==PaymentStatus.UNKNOWN||p.Status==PaymentStatus.REFUND_PENDING);
        var count=await pending.CountAsync(budget.Token);
        var stuck=await pending.CountAsync(p=>p.PendingSince<=cutoff,budget.Token);
        var oldest=await pending.MinAsync(p=>p.PendingSince,budget.Token);
        var auditCutoff=now.UtcDateTime.AddSeconds(-options.Value.IncompleteAuditAfterSeconds);
        var incomplete=await db.AuditEvents.AsNoTracking().CountAsync(b=>b.Phase=="BEGIN"&&b.CreatedAt<auditCutoff&&
            !db.AuditEvents.Any(r=>r.CorrelationId==b.CorrelationId&&r.Phase=="RESULT"),budget.Token);
        var alerts=new List<string>();
        if(stuck>0)alerts.Add("PAYMENT_STUCK");
        if(incomplete>0)alerts.Add("AUDIT_RESULT_MISSING");
        if(count>0&&!recovery.Value.Enabled)alerts.Add("RECOVERY_DISABLED_WITH_PENDING");
        if(telemetry.AuditFailureCount>0)alerts.Add("AUDIT_APPEND_FAILURE_SEEN");
        return new(now,count,stuck,oldest.HasValue?Math.Max(0,(now.UtcDateTime-oldest.Value).TotalSeconds):0,incomplete,recovery.Value.Enabled,alerts.ToArray(),telemetry.Snapshot());
    }
}
