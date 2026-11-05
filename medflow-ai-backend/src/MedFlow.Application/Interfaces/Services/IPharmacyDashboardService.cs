using MedFlow.Application.DTOs.Pharmacy;

namespace MedFlow.Application.Interfaces.Services;

public interface IPharmacyDashboardService
{
    Task<PharmacyDashboardResponseDto> GetDashboardAsync(
        long actorUserId,
        GetPharmacyDashboardRequest request,
        CancellationToken ct = default);
}
