using MedFlow.Application.DTOs.SystemAdmin;

namespace MedFlow.Application.Interfaces.Services;

public interface IRoleCatalogService
{
    Task<RoleListResponseDto> GetRoleListAsync(
        long actorUserId,
        GetRoleListRequest request,
        CancellationToken ct = default);

    Task<RoleListItemDto> GetRoleByIdAsync(
        long actorUserId,
        long roleId,
        CancellationToken ct = default);
}
