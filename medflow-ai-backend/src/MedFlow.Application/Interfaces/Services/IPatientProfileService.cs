using MedFlow.Application.DTOs.Patients;

namespace MedFlow.Application.Interfaces.Services;

public interface IPatientProfileService
{
    Task<PatientProfileResponse> GetPersonalProfileAsync(
        long userId,
        CancellationToken ct = default);

    Task<PatientProfileResponse> UpdatePersonalProfileAsync(
        long userId,
        UpdateMyPatientProfileRequest request,
        bool isPatch = false,
        string? ipAddress = null,
        CancellationToken ct = default);
}
