using MedFlow.Application.DTOs.Services;

namespace MedFlow.Application.Services.Services;

public interface IDiagnosticServiceAdminService
{
    Task<DiagnosticServiceAdminListResponseDto> GetDiagnosticServiceListAsync(
        long actorUserId,
        GetDiagnosticServiceListAdminRequest request,
        CancellationToken ct = default);
}
