using MedFlow.Application.DTOs.Diagnostic;

namespace MedFlow.Application.Interfaces.Services;

public interface IDiagnosticDashboardService
{
    Task<DiagnosticDashboardResponseDto> GetDashboardAsync(
        long actorUserId,
        GetDiagnosticDashboardRequest request,
        CancellationToken ct = default);
}
