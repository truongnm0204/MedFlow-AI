namespace MedFlow.Application.DTOs.DoctorDashboard;

public sealed record DoctorDashboardResponseDto(
    string FacilityName,
    string FacilityCode,
    string RoomCode,
    string RoomName,
    string DepartmentName,
    string ShiftLabel,
    DoctorStatsDto Stats,
    IReadOnlyList<AssignedPatientEntryDto> AssignedQueue,
    IReadOnlyList<PendingDiagnosticItemDto> PendingDiagnostics,
    IReadOnlyList<DoctorQuickActionDto> QuickActions,
    DoctorDashboardProfileDto DoctorProfile,
    DateTime GeneratedAtUtc
);
