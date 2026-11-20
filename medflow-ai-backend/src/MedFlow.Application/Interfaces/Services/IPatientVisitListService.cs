using MedFlow.Application.DTOs.Patients;

namespace MedFlow.Application.Interfaces.Services;

public interface IPatientVisitListService
{
    Task<PatientVisitListResponseDto> GetMyVisitsAsync(
        long actorUserId,
        int? limit,
        CancellationToken ct = default);
}
