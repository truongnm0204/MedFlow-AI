namespace MedFlow.Application.DTOs.Visits;

public sealed record AssignableExaminationRoomItemDto(
    long Id,
    string RoomCode,
    string Name,
    string RoomType,
    string? Location,
    long? HospitalId,
    long? DepartmentId,
    string? DepartmentCode,
    string? DepartmentName,
    int? Capacity,
    int CurrentOccupancy,
    int AvailableCapacity,
    string Status,
    bool IsCompatible,
    bool IsAvailable,
    string? UnavailableReason);
