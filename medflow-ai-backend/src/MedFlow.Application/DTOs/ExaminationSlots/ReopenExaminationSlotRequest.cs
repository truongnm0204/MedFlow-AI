namespace MedFlow.Application.DTOs.ExaminationSlots;

public sealed record ReopenExaminationSlotRequest(
    string? Reason = null,
    string? Note = null
);
