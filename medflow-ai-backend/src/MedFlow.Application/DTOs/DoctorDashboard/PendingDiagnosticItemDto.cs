namespace MedFlow.Application.DTOs.DoctorDashboard;

public sealed record PendingDiagnosticItemDto(
    string Id,
    string OrderCode,
    string PatientName,
    string PatientCode,
    string ServiceName,
    string OrderedAt,
    string Status
);
