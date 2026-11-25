namespace MedFlow.Application.DTOs.Visits;

public sealed record CancelQueueEntryRequest(
    string? Reason = null,
    int? ExpectedStateVersion = null,
    string? Notes = null,
    string? Disposition = null,
    long? TargetRoomId = null,
    long? TargetDoctorId = null);
