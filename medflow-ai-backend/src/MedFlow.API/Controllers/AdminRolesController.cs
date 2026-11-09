using System.Security.Claims;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/roles")]
[Route("api/v1/system-admin/roles")]
[Route("api/v1/roles")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminRolesController : ControllerBase
{
    private readonly IRoleCatalogService _roleCatalogService;

    public AdminRolesController(IRoleCatalogService roleCatalogService)
    {
        _roleCatalogService = roleCatalogService;
    }

    /// <summary>
    /// GET /api/v1/admin/roles — Lấy danh sách vai trò hệ thống RBAC kèm bộ lọc, phân quyền và thống kê tổng quan.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<RoleListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleListResponseDto>> GetRoleList(
        [FromQuery] GetRoleListRequest request,
        CancellationToken ct)
    {
        var result = await _roleCatalogService.GetRoleListAsync(GetUserId(), request, ct);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/v1/admin/roles/permissions — Lấy ma trận vai trò, danh mục quyền hạn hệ thống và thống kê RBAC.
    /// </summary>
    [HttpGet("permissions")]
    [ProducesResponseType<RolePermissionsCatalogResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RolePermissionsCatalogResponseDto>> GetRolePermissionsCatalog(
        CancellationToken ct)
    {
        var result = await _roleCatalogService.GetRolePermissionsCatalogAsync(GetUserId(), ct);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/v1/admin/roles/{id} — Lấy thông tin chi tiết một vai trò theo mã định danh.
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<RoleListItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleListItemDto>> GetRoleById(
        [FromRoute] long id,
        CancellationToken ct)
    {
        var result = await _roleCatalogService.GetRoleByIdAsync(GetUserId(), id, ct);
        return Ok(result);
    }

    /// <summary>
    /// PUT/PATCH /api/v1/admin/roles/{roleId} — Cập nhật thông tin vai trò hệ thống RBAC.
    /// </summary>
    [HttpPut("{roleId}")]
    [HttpPatch("{roleId}")]
    [ProducesResponseType<RoleListItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleListItemDto>> UpdateRole(
        [FromRoute] string roleId,
        [FromBody] UpdateRoleRequest request,
        CancellationToken ct)
    {
        var result = await _roleCatalogService.UpdateRoleAsync(
            GetUserId(),
            roleId,
            request,
            GetIp(),
            ct);
        return Ok(result);
    }

    /// <summary>
    /// PUT /api/v1/admin/roles/{roleId}/permissions — Gán hoặc thu hồi danh sách quyền hạn cho một vai trò.
    /// </summary>
    [HttpPut("{roleId}/permissions")]
    [ProducesResponseType<RoleListItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleListItemDto>> AssignRolePermissions(
        [FromRoute] string roleId,
        [FromBody] AssignRolePermissionsRequest request,
        CancellationToken ct)
    {
        var result = await _roleCatalogService.AssignRolePermissionsAsync(
            GetUserId(),
            roleId,
            request,
            GetIp(),
            ct);
        return Ok(result);
    }

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId))
        {
            throw new UnauthorizedAccessException("Token không hợp lệ");
        }

        return userId;
    }

    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();
}
