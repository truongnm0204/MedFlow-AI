using MedFlow.Application.DTOs.SystemAdmin;

namespace MedFlow.Application.Interfaces.Services;

public interface IStaffAccountAdminService
{
    Task<DeactivateStaffAccountResponseDto> DeactivateAsync(
        long actorUserId,
        long staffId,
        DeactivateStaffAccountRequest? request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
