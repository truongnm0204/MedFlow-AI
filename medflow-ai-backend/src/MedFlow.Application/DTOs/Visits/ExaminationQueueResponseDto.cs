namespace MedFlow.Application.DTOs.Visits;

public sealed record ExaminationQueueResponseDto(
    string QueueDate,
    string FacilityName,
    string ShiftLabel,
    DateTime GeneratedAtUtc,
    ExaminationQueueStatsDto Stats,
    IReadOnlyList<ServicePointQueueDto> ServicePoints);

public sealed record ExaminationQueueStatsDto(
    int TotalEntries,
    int WaitingCount,
    int CalledOrServingCount,
    int CompletedCount,
    int SkippedCount,
    int ActiveServicePoints);

public sealed record ServicePointQueueDto(
    string ServicePointId,
    string ServicePointName,
    string RoomCode,
    string RoomName,
    string DepartmentName,
    string DoctorName,
    bool IsActive,
    string? CurrentServingTicketNo,
    string? NextTicketNo,
    int WaitingCount,
    int InProgressCount,
    int CompletedCount,
    int SkippedCount,
    IReadOnlyList<ExaminationQueueEntryDto> Entries);

public sealed record ExaminationQueueEntryDto(
    string Id,
    int QueueNumber,
    string TicketNo,
    int ServingOrder,
    string VisitCode,
    string PatientName,
    string PatientCode,
    string VisitType,
    string ServicePointId,
    string RoomCode,
    string RoomName,
    string DepartmentName,
    string DoctorName,
    string Status,
    string Priority,
    string QueueType,
    string PresenceStatus,
    string ArrivalClass,
    string? ScheduledAt,
    string CheckedInAt,
    string? CalledAt,
    string? StartedAt,
    string? BlockedReason,
    bool IsEligible = false,
    bool CanSafeBackfill = false,
    bool IsAppointmentProtected = false,
    string? HoldReason = null,
    bool IsBackfilled = false,
    string? BackfillReason = null,
    string? BackfillUpdatedAt = null);
