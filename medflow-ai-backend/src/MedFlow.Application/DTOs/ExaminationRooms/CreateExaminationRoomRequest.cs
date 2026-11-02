namespace MedFlow.Application.DTOs.ExaminationRooms;

public sealed record CreateExaminationRoomRequest(
    string? RoomCode = null,
    string? Name = null,
    long? DepartmentId = null,
    long? HospitalId = null,
    string? RoomType = null,
    string? Location = null,
    int? Capacity = null,
    string? Status = null,
    string? Code = null
);
