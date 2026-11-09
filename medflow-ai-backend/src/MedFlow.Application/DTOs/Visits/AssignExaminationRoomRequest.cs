namespace MedFlow.Application.DTOs.Visits;

public sealed record AssignExaminationRoomRequest(
    long? ExaminationRoomId = null,
    long? RoomId = null,
    string? RoomCode = null,
    string? Priority = null,
    string? Reason = null,
    string? Note = null,
    bool? PriorityVerificationConfirmed = null);
