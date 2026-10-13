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

    public void Add(Guid actorUserId, string action, string entityType,
        Guid? entityId, object? newValue, string? ipAddress)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId == Guid.Empty ? null : actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue),
            IpAddress = IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
        });
    }
}
