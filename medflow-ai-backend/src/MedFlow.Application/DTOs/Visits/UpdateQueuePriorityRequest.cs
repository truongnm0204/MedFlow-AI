namespace MedFlow.Application.DTOs.Visits;

public sealed record UpdateQueuePriorityRequest(
    string? Priority = null,
    string? Reason = null,
    bool? VerificationConfirmed = null,
    int? ExpectedStateVersion = null);
