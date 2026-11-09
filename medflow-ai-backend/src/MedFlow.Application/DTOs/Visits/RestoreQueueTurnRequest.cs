namespace MedFlow.Application.DTOs.Visits;

public sealed record RestoreQueueTurnRequest(
    string? ServicePointId,
    bool ConfirmPresent,
    string? Reason,
    string? Notes,
    int? ExpectedStateVersion);
