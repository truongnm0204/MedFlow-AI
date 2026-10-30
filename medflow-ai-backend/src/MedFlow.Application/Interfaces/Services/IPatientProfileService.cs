using MedFlow.Application.DTOs.Patients;

namespace MedFlow.Application.Interfaces.Services;

public interface IPatientProfileService
{
    Task<PatientProfileResponse> GetPersonalProfileAsync(
        long userId,
        CancellationToken ct = default);
}
