using System.Net;

namespace MedFlow.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public long? ActorUserId { get; set; }
    public long? HospitalId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    /// <summary>Entity PK serialised as text — schema column is varchar(80).</summary>
    public string? EntityId { get; set; }
    public string? Reason { get; set; }
    /// <summary>JSONB — merged old/new value payload.</summary>
    public string? ChangeData { get; set; }
    public IPAddress? IpAddress { get; set; }
    public DateTime OccurredAt { get; set; }

    public User? ActorUser { get; set; }
}
