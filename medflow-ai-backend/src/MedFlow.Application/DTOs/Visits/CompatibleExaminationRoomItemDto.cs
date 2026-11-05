namespace MedFlow.Application.DTOs.Visits;

public sealed record CompatibleExaminationRoomItemDto(
    long RoomId,
    string RoomCode,
    string RoomName,
    long? HospitalId,
    long? DepartmentId,
    string? DepartmentCode,
    string? DepartmentName,
    string RoomType,
    string? Location,
    int Capacity,
    int ActiveVisitCount,
    int AvailableCapacity,
    string Status,
    bool IsCurrentRoom,
    bool IsCompatible,
    bool IsAvailable,
    string? UnavailableReason);
