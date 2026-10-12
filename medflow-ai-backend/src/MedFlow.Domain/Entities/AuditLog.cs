using System.Net;

namespace MedFlow.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public IPAddress? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }

    public User? ActorUser { get; set; }
}
