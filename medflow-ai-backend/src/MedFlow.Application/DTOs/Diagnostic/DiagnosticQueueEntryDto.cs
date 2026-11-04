namespace MedFlow.Application.DTOs.Diagnostic;

public sealed record DiagnosticQueueEntryDto(
    string Id,
    string OrderCode,
    string TicketNo,
    string PatientName,
    string PatientCode,
    string ServiceName,
    string Category,
    string Room,
    string OrderingDoctor,
    string OrderedAt,
    string Priority,
    string Status
);
