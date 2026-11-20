using MedFlow.Application.DTOs.SystemAdmin;

namespace MedFlow.Application.Interfaces.Services;

public interface IStaffAccountService
{
    Task<CreateStaffAccountResponse> CreateAsync(
        long actorUserId,
        CreateStaffAccountRequest request,
        string? ipAddress,
        CancellationToken ct = default);
}
