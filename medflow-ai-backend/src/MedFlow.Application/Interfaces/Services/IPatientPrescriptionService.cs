using MedFlow.Application.DTOs.Patients;

namespace MedFlow.Application.Interfaces.Services;

public interface IPatientPrescriptionService
{
    Task<PatientPrescriptionListResponseDto> GetMyPrescriptionsAsync(
        long actorUserId,
        GetPatientPrescriptionsRequest? request,
        CancellationToken ct = default,
        string? ipAddress = null);

    Task<PatientPrescriptionDetailDto> GetMyPrescriptionAsync(
        long actorUserId,
        string? idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null);
}
