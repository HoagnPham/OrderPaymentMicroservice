using System.ComponentModel.DataAnnotations;

namespace OrderPaymentMicroservice.Operations;

public sealed class OperationsOptions
{
    [Range(5,86400)] public int StuckAfterSeconds { get; set; } = 300;
    [Range(5,86400)] public int IncompleteAuditAfterSeconds { get; set; } = 300;
    [Range(200,10000)] public int QueryTimeoutMilliseconds { get; set; } = 2000;
    public bool AlertPollingEnabled { get; set; } = true;
    [Range(5,3600)] public int AlertPollSeconds { get; set; } = 30;
}
