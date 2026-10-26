using System.Net;
using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;

namespace MedFlow.Application.Services.Auth;

/// <summary>
/// Adds <see cref="AuditLog"/> rows to the context. Persistence is left to the
/// caller's SaveChanges/transaction — logging must never commit on its own.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly IApplicationDbContext _db;

    public AuditLogger(IApplicationDbContext db) => _db = db;

    public void Add(long? actorUserId, string action, string entityType,
        string? entityId, object? changeData, string? ipAddress)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ChangeData = changeData is null ? null : JsonSerializer.Serialize(changeData),
            IpAddress = IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
            OccurredAt = DateTime.UtcNow,
        });
    }
}
