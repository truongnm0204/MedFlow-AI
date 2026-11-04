namespace MedFlow.Application.DTOs.Visits;

public sealed record QueueStatusTransitionHistoryItemDto(
    long AuditLogId,
    long QueueEntryId,
    long? VisitId,
    string? VisitCode,
    string PreviousStatus,
    string TargetStatus,
    string? PreviousPresenceStatus,
    string? UpdatedPresenceStatus,
    string? PreviousPriority,
    string? UpdatedPriority,
    int? PreviousStateVersion,
    int UpdatedStateVersion,
    string? Reason,
    string? BlockedReason,
    string? Notes,
    long? UpdatedByUserId,
    string? UpdatedByStaffCode,
    string? UpdatedByName,
    DateTime UpdatedAtUtc);
