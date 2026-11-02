namespace MedFlow.Application.DTOs.ExaminationRooms;

public sealed record ExaminationRoomItemDto(
    long Id,
    string RoomCode,
    string Name,
    long? HospitalId,
    string Facility,
    long? DepartmentId,
    string? DepartmentCode,
    string? DepartmentName,
    string RoomType,
    string? Location,
    int? Capacity,
    string Status,
    bool IsAvailable,
    bool IsActive
);
