namespace MedFlow.Application.DTOs.Diagnostic;

public sealed record DiagnosticQuickActionDto(
    string Id,
    string Label,
    string Description,
    string Icon,
    string Accent,
    string UcCode
);
