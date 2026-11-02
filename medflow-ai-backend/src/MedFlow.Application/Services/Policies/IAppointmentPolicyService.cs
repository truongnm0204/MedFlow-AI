using MedFlow.Application.DTOs.Policies;

namespace MedFlow.Application.Services.Policies;

public interface IAppointmentPolicyService
{
    Task<AppointmentPolicyDto> GetEffectiveAsync(long hospitalId, CancellationToken ct = default);
    Task<AppointmentPolicyDto> GetForAdminAsync(long userId, CancellationToken ct = default);
    Task<AppointmentPolicyDto> UpdateForAdminAsync(
        long userId,
        UpdateAppointmentPolicyRequest request,
        string? ipAddress,
        CancellationToken ct = default);
}
