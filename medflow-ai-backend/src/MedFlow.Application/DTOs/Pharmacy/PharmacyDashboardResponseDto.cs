namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record PharmacyDashboardResponseDto(
    string FacilityName,
    string FacilityCode,
    string CounterLabel,
    string CounterCode,
    string ShiftLabel,
    PharmacyStatsDto Stats,
    IReadOnlyList<PrescriptionQueueEntryDto> PrescriptionQueue,
    IReadOnlyList<PharmacyQuickActionDto> QuickActions,
    IReadOnlyList<FefoBatchAlertItemDto> FefoBatchAlerts,
    PharmacistProfileDto PharmacistProfile,
    DateTime GeneratedAtUtc
);
