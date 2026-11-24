using MedFlow.Application.DTOs.Pharmacy;

namespace MedFlow.Application.Services.Pharmacy;

public interface IFefoBatchSelectionService
{
    Task<FefoBatchSelectionContextDto> GetFefoBatchesAsync(
        long actorUserId,
        string idOrCode,
        bool includeUnavailable = true,
        CancellationToken ct = default,
        string? ipAddress = null);

    Task<SelectFefoBatchesResponseDto> SelectFefoBatchesAsync(
        long actorUserId,
        string idOrCode,
        SelectFefoBatchesRequest? request,
        CancellationToken ct = default,
        string? ipAddress = null);
}
