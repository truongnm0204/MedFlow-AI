namespace MedFlow.Application.DTOs.Diagnostic;

public sealed record DiagnosticStatsDto(
    int PendingOrders,
    int InQueue,
    int InExecution,
    int CompletedToday
);
