namespace MedFlow.Application.DTOs.Visits;

public sealed record MarkMissedTurnRequest(
    string? ServicePointId,
    string? Reason,
    string? Notes,
    int? ExpectedStateVersion);
