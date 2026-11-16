using MedFlow.Application.DTOs.SystemAdmin;

namespace MedFlow.Application.Interfaces.Services;

public interface IStaffAccountListService
{
    Task<StaffAccountListResponseDto> GetStaffAccountsAsync(
        long actorUserId,
        GetStaffAccountListRequest request,
        CancellationToken ct = default);
}
