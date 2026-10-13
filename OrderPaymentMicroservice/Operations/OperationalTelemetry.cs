namespace OrderPaymentMicroservice.Operations;

/// <summary>Process-local counters; no resource IDs or secrets in labels.</summary>
public sealed class OperationalTelemetry
{
    private long _auditFailures, _scanFailures, _attempts, _resolved, _lastScan;
    public void AuditFailure()=>Interlocked.Increment(ref _auditFailures);
    public long AuditFailureCount=>Interlocked.Read(ref _auditFailures);
    public void ScanFailure()=>Interlocked.Increment(ref _scanFailures);
    public void Attempt(bool resolved){Interlocked.Increment(ref _attempts);if(resolved)Interlocked.Increment(ref _resolved);}
    public void ScanCompleted()=>Interlocked.Exchange(ref _lastScan,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    public object Snapshot()=>new { audit_append_failures=Interlocked.Read(ref _auditFailures), recovery_scan_failures=Interlocked.Read(ref _scanFailures),
        recovery_attempts=Interlocked.Read(ref _attempts), recovery_resolved=Interlocked.Read(ref _resolved),
        last_completed_scan_unix_ms=Interlocked.Read(ref _lastScan),scope="this_process_since_start" };
}
