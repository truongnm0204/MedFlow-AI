namespace MedFlow.Application.DTOs.ExaminationSlots;

public sealed record ExaminationSlotMetricsDto(
    int Total,
    int OpenCount,
    int BookedCount,
    int UpcomingCount,
    int ReviewCount
);
