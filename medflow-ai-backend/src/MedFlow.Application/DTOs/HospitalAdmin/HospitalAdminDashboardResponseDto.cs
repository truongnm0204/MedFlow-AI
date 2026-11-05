namespace MedFlow.Application.DTOs.HospitalAdmin;

public sealed record HospitalAdminDashboardResponseDto(
    string FacilityName,
    string FacilityCode,
    string ShiftLabel,
    HospitalAdminStatsDto Stats,
    IReadOnlyList<HospitalConfigWorklistItemDto> Worklist,
    IReadOnlyList<HospitalAdminQuickActionDto> QuickActions,
    IReadOnlyList<HospitalPolicyAlertItemDto> PolicyAlerts,
    HospitalAdminProfileDto AdminProfile,
    DateTime GeneratedAtUtc
);
