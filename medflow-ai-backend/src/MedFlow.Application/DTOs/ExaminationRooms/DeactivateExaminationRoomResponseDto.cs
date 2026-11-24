namespace MedFlow.Application.DTOs.ExaminationRooms;

public sealed record DeactivateExaminationRoomResponseDto(
    long Id,
    string RoomCode,
    string Name,
    long? HospitalId,
    string Facility,
    bool IsActive,
    bool IsAvailable,
    string Status,
    string Message,
    ExaminationRoomDependencySummaryDto? Dependencies = null,
    int ClosedSlotsCount = 0
);
