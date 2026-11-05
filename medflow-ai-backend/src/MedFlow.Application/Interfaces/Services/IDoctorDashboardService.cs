using MedFlow.Application.DTOs.DoctorDashboard;

namespace MedFlow.Application.Interfaces.Services;

public interface IDoctorDashboardService
{
    Task<DoctorDashboardResponseDto> GetDashboardAsync(
        long actorUserId,
        GetDoctorDashboardRequest request,
        CancellationToken ct = default);
}
