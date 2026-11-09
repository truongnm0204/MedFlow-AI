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

    Task<RolePermissionsCatalogResponseDto> GetRolePermissionsCatalogAsync(
        long actorUserId,
        CancellationToken ct = default);

    Task<RoleListItemDto> AssignRolePermissionsAsync(
        long actorUserId,
        string roleIdentifier,
        AssignRolePermissionsRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<RoleListItemDto> UpdateRoleAsync(
        long actorUserId,
        string roleIdentifier,
        UpdateRoleRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<RoleListItemDto> CreateRoleAsync(
        long actorUserId,
        CreateRoleRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
