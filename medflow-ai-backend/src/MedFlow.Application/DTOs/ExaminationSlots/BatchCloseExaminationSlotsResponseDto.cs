namespace MedFlow.Application.DTOs.ExaminationSlots;

public sealed record BatchCloseExaminationSlotsResponseDto(
    bool Success,
    int Count,
    int TotalAffectedBookings,
    string Message,
    IReadOnlyList<ExaminationSlotAdminItemDto> Slots
);
