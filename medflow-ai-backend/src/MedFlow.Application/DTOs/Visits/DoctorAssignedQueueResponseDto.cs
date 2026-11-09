namespace MedFlow.Application.DTOs.Visits;

public sealed record DoctorAssignedQueueResponseDto(
    string QueueDate,
    string FacilityName,
    string ShiftLabel,
    DateTime GeneratedAtUtc,
    string DoctorName,
    DoctorAssignedQueueStatsDto Stats,
    IReadOnlyList<DoctorAssignedServicePointDto> ServicePoints);

public sealed record DoctorAssignedQueueStatsDto(
    int TotalEntries,
    int InProgressCount,
    int CalledCount,
    int EligibleCount,
    int NotEligibleCount,
    int SkippedCount,
    int CompletedCount);

public sealed record DoctorAssignedServicePointDto(
    string ServicePointId,
    string ServicePointName,
    string RoomCode,
    string RoomName,
    string DepartmentName,
    bool IsActive,
    string? CurrentServingTicketNo,
    string? NextEligibleTicketNo,
    DoctorAssignedQueueStatsDto Stats,
    IReadOnlyList<ExaminationQueueEntryDto> Entries);
