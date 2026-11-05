using MedFlow.Application.DTOs.HospitalAdmin;

namespace MedFlow.Application.Interfaces.Services;

public interface IHospitalAdminDashboardService
{
    Task<HospitalAdminDashboardResponseDto> GetDashboardAsync(
        long actorUserId,
        GetHospitalAdminDashboardRequest request,
        CancellationToken ct = default);
}
