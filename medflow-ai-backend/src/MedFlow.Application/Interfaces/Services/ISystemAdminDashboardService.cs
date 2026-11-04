using MedFlow.Application.DTOs.SystemAdmin;

namespace MedFlow.Application.Interfaces.Services;

public interface ISystemAdminDashboardService
{
    Task<SystemAdminDashboardResponseDto> GetDashboardAsync(
        long actorUserId,
        GetSystemAdminDashboardRequest request,
        CancellationToken ct = default);
}
