namespace MedFlow.Application.DTOs.Visits;

public sealed record UpdateQueueStatusRequest(
    string? TargetStatus = null,
    string? Status = null,
    string? PresenceStatus = null,
    string? Reason = null,
    string? BlockedReason = null,
    string? Notes = null,
    int? ExpectedStateVersion = null);
