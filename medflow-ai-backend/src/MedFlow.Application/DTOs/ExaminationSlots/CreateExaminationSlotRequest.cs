namespace MedFlow.Application.DTOs.ExaminationSlots;

public sealed record CreateExaminationSlotRequest(
    long? ServiceId = null,
    string? ServiceCode = null,
    long? DoctorId = null,
    string? DoctorCode = null,
    long? RoomId = null,
    string? RoomCode = null,
    DateTime? StartsAt = null,
    DateTime? EndsAt = null,
    string? Date = null,
    string? StartTime = null,
    string? EndTime = null,
    int? Capacity = null,
    string? Status = null
);
