namespace MedFlow.Application.DTOs.Visits;

public sealed record AssignDoctorToVisitRequest(
    long? DoctorId = null,
    string? DoctorCode = null,
    long? ExaminationRoomId = null,
    long? RoomId = null,
    string? RoomCode = null,
    string? RoomName = null,
    long? ServiceId = null,
    string? ServiceCode = null,
    string? Priority = null,
    string? Reason = null,
    string? Note = null,
    bool? PriorityVerificationConfirmed = null);
