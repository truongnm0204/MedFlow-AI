namespace MedFlow.Application.Interfaces.Services;

/// <summary>
/// Queues an audit entry into the current DbContext unit of work.
/// Never calls SaveChanges — the calling service controls the transaction boundary.
/// </summary>
public interface IAuditLogger
{
    /// <param name="actorUserId">null for system actions</param>
    /// <param name="entityId">entity PK serialised as text (varchar(80) in v4.1)</param>
    /// <param name="changeData">payload to serialise into <c>change_data</c> jsonb</param>
    void Add(long? actorUserId, string action, string entityType,
        string? entityId, object? changeData, string? ipAddress);
}
