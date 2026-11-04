namespace MedFlow.Application.DTOs.Visits;

public sealed record TransferVisitRoomRequest(
    long? TargetRoomId = null,
    string? TargetRoomCode = null,
    string? Reason = null,
    string? Notes = null,
    long? TargetDoctorId = null);
