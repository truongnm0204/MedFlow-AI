using MedFlow.Application.DTOs.SystemAdmin;

namespace MedFlow.Application.Interfaces.Services;

public interface IStaffAccountUpdateService
{
    Task<UpdateStaffAccountResponse> UpdateAsync(
        long actorUserId,
        string staffIdentifier,
        UpdateStaffAccountRequest request,
        string? ipAddress,
        CancellationToken ct = default);
}
