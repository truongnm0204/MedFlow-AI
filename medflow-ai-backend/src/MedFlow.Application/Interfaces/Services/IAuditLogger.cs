namespace MedFlow.Application.Interfaces.Services;

/// <summary>
/// Queues an audit entry into the current DbContext unit of work.
/// Never calls SaveChanges — the calling service controls the transaction boundary.
/// </summary>
public interface IAuditLogger
{
    void Add(Guid actorUserId, string action, string entityType,
        Guid? entityId, object? newValue, string? ipAddress);
}
