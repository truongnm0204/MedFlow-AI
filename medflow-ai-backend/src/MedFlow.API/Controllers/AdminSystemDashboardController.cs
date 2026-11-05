using System.Security.Claims;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/system-dashboard")]
[Route("api/v1/system-admin/dashboard")]
[Route("api/v1/admin/dashboard/system")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminSystemDashboardController : ControllerBase
{
    private readonly ISystemAdminDashboardService _dashboardService;

    public AdminSystemDashboardController(ISystemAdminDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// GET /api/v1/admin/system-dashboard — Lấy thông tin tổng quan bảng điều khiển Quản trị hệ thống (System Admin Dashboard).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SystemAdminDashboardResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SystemAdminDashboardResponseDto>> GetDashboard(
        [FromQuery] GetSystemAdminDashboardRequest request,
        CancellationToken ct)
    {
        var result = await _dashboardService.GetDashboardAsync(GetUserId(), request, ct);
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
