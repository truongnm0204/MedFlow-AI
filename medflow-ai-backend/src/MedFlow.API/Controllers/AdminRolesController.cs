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
}
