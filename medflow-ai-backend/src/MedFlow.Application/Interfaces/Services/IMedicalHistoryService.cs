using MedFlow.Application.DTOs.Consultation;

namespace MedFlow.Application.Interfaces.Services;

public interface IMedicalHistoryService
{
    Task<PatientMedicalHistoryResponseDto> GetByPatientAsync(
        long actorUserId,
        string patientIdOrCode,
        GetPatientMedicalHistoryRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<PatientMedicalHistoryResponseDto> GetByVisitAsync(
        long actorUserId,
        string visitIdOrCode,
        GetPatientMedicalHistoryRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
