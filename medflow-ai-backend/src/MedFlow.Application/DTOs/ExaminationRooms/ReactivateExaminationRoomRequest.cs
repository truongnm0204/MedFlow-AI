namespace MedFlow.Application.DTOs.ExaminationRooms;

public sealed record ReactivateExaminationRoomRequest(
    string? Reason = null,
    string? Note = null
);
