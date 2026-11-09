using MedFlow.Application.DTOs.Policies;

namespace MedFlow.Application.Services.Policies;

public interface IQueuePolicyService
{
    Task<QueuePolicyDto> GetEffectiveAsync(long hospitalId, CancellationToken ct = default);
    Task<QueuePolicyDto> GetForAdminAsync(long userId, CancellationToken ct = default);
    Task<QueuePolicyDto> UpdateForAdminAsync(
        long userId,
        UpdateQueuePolicyRequest request,
        string? ipAddress,
        CancellationToken ct = default);
}
