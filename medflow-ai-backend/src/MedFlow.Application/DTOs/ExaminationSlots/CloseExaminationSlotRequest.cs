namespace MedFlow.Application.DTOs.ExaminationSlots;

public sealed record CloseExaminationSlotRequest(
    string? SlotId = null,
    string? ReasonCode = null,
    string? ReasonText = null,
    string? Reason = null,
    string? Note = null,
    bool NotifyDoctor = true,
    bool NotifyPatients = true,
    string? AffectedBookingsAction = null,
    bool? ConfirmUnderstood = null
);
