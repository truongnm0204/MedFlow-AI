namespace MedFlow.Application.DTOs.ExaminationRooms;

public sealed record GetExaminationRoomListRequest(
    string? Keyword = null,
    long? DepartmentId = null,
    long? HospitalId = null,
    string? RoomType = null,
    string? Status = null,
    bool? IsAvailable = null,
    int Page = 1,
    int PageSize = 10
);
