namespace MedFlow.Application.DTOs.Diagnostic;

public sealed record GetDiagnosticDashboardRequest(
    int QueueLimit = 20,
    int PendingResultLimit = 10,
    string? Category = null,
    string? Status = null,
    string? Priority = null,
    string? Keyword = null
);
