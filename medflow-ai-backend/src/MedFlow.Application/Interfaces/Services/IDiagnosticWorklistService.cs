using MedFlow.Application.DTOs.Diagnostic;

namespace MedFlow.Application.Interfaces.Services;

public interface IDiagnosticWorklistService
{
    Task<DiagnosticWorklistResponseDto> GetWorklistAsync(
        long actorUserId,
        GetDiagnosticWorklistRequest? request = null,
        CancellationToken ct = default);

    Task<DiagnosticWorklistItemDto> GetWorklistOrderDetailAsync(
        long actorUserId,
        string orderIdOrCode,
        CancellationToken ct = default);
}
