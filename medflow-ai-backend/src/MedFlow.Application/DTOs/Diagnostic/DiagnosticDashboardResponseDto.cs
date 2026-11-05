namespace MedFlow.Application.DTOs.Diagnostic;

public sealed record DiagnosticDashboardResponseDto(
    string FacilityName,
    string FacilityCode,
    string DepartmentName,
    string ShiftLabel,
    DiagnosticStatsDto Stats,
    IReadOnlyList<DiagnosticQueueEntryDto> Queue,
    IReadOnlyList<PendingResultEntryDto> PendingResults,
    IReadOnlyList<DiagnosticQuickActionDto> QuickActions,
    DiagnosticStaffProfileDto StaffProfile,
    DateTime GeneratedAtUtc
);
