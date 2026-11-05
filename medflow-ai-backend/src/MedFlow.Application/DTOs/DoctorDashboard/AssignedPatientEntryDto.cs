namespace MedFlow.Application.DTOs.DoctorDashboard;

public sealed record AssignedPatientEntryDto(
    string Id,
    string QueueNo,
    string PatientName,
    string PatientCode,
    int Age,
    string Gender,
    string ChiefComplaint,
    string AssignedAt,
    string Priority,
    string Status
);
