using MedFlow.Application.DTOs.Patients;

namespace MedFlow.Application.Interfaces.Services;

public interface IPatientMedicalRecordService
{
    Task<PatientMedicalRecordListResponseDto> GetMyMedicalRecordsAsync(
        long actorUserId,
        GetPatientMedicalRecordsRequest? request,
        CancellationToken ct = default,
        string? ipAddress = null);

    Task<PatientMedicalRecordDetailDto> GetMyMedicalRecordAsync(
        long actorUserId,
        string? idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null);
}
