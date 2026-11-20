namespace MedFlow.Application.DTOs.Doctors;

public sealed record DeactivateDoctorRequest(
    string? Reason = null,
    bool ConfirmUnderstood = false
);
