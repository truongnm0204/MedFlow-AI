namespace MedFlow.Application.DTOs.Visits;

public sealed record CreateReturnToDoctorQueueEntryRequest(
    string? ServicePointId = null,
    bool ConfirmPresent = true,
    bool? DiagnosticResultsReady = null,
    string? Reason = null,
    string? Notes = null,
    int? ExpectedStateVersion = null);
