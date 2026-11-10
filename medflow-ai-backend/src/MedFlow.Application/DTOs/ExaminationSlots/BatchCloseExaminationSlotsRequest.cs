namespace MedFlow.Application.DTOs.ExaminationSlots;

public sealed record BatchCloseExaminationSlotsRequest(
    IReadOnlyList<string>? Ids = null,
    IReadOnlyList<long>? SlotIds = null,
    string? ReasonCode = null,
    string? ReasonText = null,
    string? Reason = null,
    string? Note = null,
    bool NotifyDoctor = true,
    bool NotifyPatients = true,
    string? AffectedBookingsAction = null,
    bool? ConfirmUnderstood = null
);
