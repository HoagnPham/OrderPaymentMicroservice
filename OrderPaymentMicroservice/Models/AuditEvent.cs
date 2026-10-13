namespace OrderPaymentMicroservice.Models;

/// <summary>Allowlisted event fields only: no request/response bodies, headers, keys or exception text.</summary>
public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CorrelationId { get; set; }
    public string ActorKind { get; set; } = "USER";
    public int? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public int? ResourceId { get; set; }
    public string Phase { get; set; } = "BEGIN";
    public string Outcome { get; set; } = "STARTED";
    public int? HttpStatusCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
