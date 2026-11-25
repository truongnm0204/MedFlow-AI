namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record GetAuditLogsRequest(
    string? Keyword = null,
    string? Query = null,
    string? Action = null,
    string? EntityType = null,
    long? ActorUserId = null,
    long? HospitalId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int Page = 1,
    int PageSize = 20);

public sealed record AuditLogMetricsDto(
    int TotalLogs,
    int Last24HoursCount,
    int AuthEventsCount,
    int ConfigAndRbacEventsCount);

public sealed record AuditLogHospitalOptionDto(
    long Id,
    string Code,
    string Name);

public sealed record AuditLogListItemDto(
    long Id,
    string AuditCode,
    long? ActorUserId,
    string? ActorEmail,
    string? ActorFullName,
    long? HospitalId,
    string? HospitalName,
    string Action,
    string Category,
    string EntityType,
    string? EntityId,
    string? Reason,
    string? IpAddress,
    DateTime OccurredAt,
    DateTime OccurredAtHospitalLocal,
    bool HasMetadata,
    string? ChangeData);

public sealed record AuditLogDetailDto(
    long Id,
    string AuditCode,
    long? ActorUserId,
    string? ActorEmail,
    string? ActorFullName,
    long? HospitalId,
    string? HospitalName,
    string Action,
    string Category,
    string EntityType,
    string? EntityId,
    string? Reason,
    string? IpAddress,
    DateTime OccurredAt,
    DateTime OccurredAtHospitalLocal,
    string? ChangeData,
    IReadOnlyList<string> RedactedFields);

public sealed record AuditLogListResponseDto(
    IReadOnlyList<AuditLogListItemDto> Data,
    IReadOnlyList<AuditLogListItemDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    AuditLogMetricsDto Metrics,
    IReadOnlyList<string> AvailableActions,
    IReadOnlyList<string> AvailableEntityTypes,
    IReadOnlyList<AuditLogHospitalOptionDto> AvailableHospitals);
