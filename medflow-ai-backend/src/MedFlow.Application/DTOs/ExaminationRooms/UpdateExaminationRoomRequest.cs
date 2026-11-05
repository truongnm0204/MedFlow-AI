namespace MedFlow.Application.DTOs.ExaminationRooms;

public sealed record UpdateExaminationRoomRequest(
    string? RoomCode = null,
    string? Name = null,
    long? DepartmentId = null,
    string? Department = null,
    long? HospitalId = null,
    string? RoomType = null,
    string? Type = null,
    string? Location = null,
    int? Capacity = null,
    string? Status = null,
    bool? IsActive = null,
    bool? IsAvailable = null,
    string? Code = null
);
