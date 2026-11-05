namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record PharmacyStatsDto(
    int PendingPrescriptions,
    int PreparingCount,
    int ReadyToDispense,
    int DispensedToday
);
