namespace MedFlow.Application.DTOs.Doctors;

public sealed record ReactivateDoctorRequest(
    string? Reason = null
);
