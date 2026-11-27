namespace MedFlow.Application.DTOs.Visits;

public sealed record CreateReturnToDoctorQueueEntryRequest(
    string? ServicePointId = null,
    bool ConfirmPresent = false,
    bool? DiagnosticResultsReady = null,
    string? Reason = null,
    string? Notes = null,
    int? ExpectedStateVersion = null);
