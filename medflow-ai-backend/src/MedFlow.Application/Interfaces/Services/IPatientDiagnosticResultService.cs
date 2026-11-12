using MedFlow.Application.DTOs.Patients;

namespace MedFlow.Application.Interfaces.Services;

public interface IPatientDiagnosticResultService
{
    Task<PatientDiagnosticResultListResponseDto> GetMyResultsAsync(
        long actorUserId,
        GetPatientDiagnosticResultsRequest? request,
        CancellationToken ct = default);

    Task<PatientDiagnosticResultDetailDto> GetMyResultAsync(
        long actorUserId,
        string? idOrCode,
        CancellationToken ct = default);
}
