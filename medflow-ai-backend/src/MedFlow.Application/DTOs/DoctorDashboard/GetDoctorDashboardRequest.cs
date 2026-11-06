namespace MedFlow.Application.DTOs.DoctorDashboard;

public sealed record GetDoctorDashboardRequest(
    int QueueLimit = 20,
    int DiagnosticLimit = 10,
    string? Status = null,
    string? Priority = null,
    string? Keyword = null,
    string? QueueType = null
);
