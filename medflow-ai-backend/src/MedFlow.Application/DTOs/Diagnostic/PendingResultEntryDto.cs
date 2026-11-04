namespace MedFlow.Application.DTOs.Diagnostic;

public sealed record PendingResultEntryDto(
    string Id,
    string OrderCode,
    string PatientName,
    string ServiceName,
    string OrderingDoctor,
    string UpdatedAt,
    string Status
);
