namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record SystemAdminAuditLogItemDto(
    long Id,
    long? ActorUserId,
    string? ActorEmail,
    long? HospitalId,
    string Action,
    string EntityType,
    string? EntityId,
    string? Reason,
    string? IpAddress,
    DateTime OccurredAt
);
