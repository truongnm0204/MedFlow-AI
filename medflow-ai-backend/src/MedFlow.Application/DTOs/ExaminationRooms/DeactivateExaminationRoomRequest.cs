namespace MedFlow.Application.DTOs.ExaminationRooms;

public sealed record DeactivateExaminationRoomRequest(
    string? Reason = null,
    string? Note = null,
    bool ConfirmUnderstood = false
);
