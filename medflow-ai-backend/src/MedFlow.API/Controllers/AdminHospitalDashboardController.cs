using System.Security.Claims;
using MedFlow.Application.DTOs.HospitalAdmin;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/hospital-dashboard")]
[Route("api/v1/hospital-admin/dashboard")]
[Route("api/v1/admin/dashboard/hospital")]
[Authorize(Roles = "HOSPITAL_ADMIN")]
public sealed class AdminHospitalDashboardController : ControllerBase
{
    private readonly IHospitalAdminDashboardService _dashboardService;

    public AdminHospitalDashboardController(IHospitalAdminDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// GET /api/v1/admin/hospital-dashboard/summary — Lấy thông tin tổng quan bảng điều khiển Quản trị bệnh viện (Hospital Admin Dashboard).
    /// </summary>
    [HttpGet]
    [HttpGet("summary")]
    [ProducesResponseType<HospitalAdminDashboardResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HospitalAdminDashboardResponseDto>> GetDashboard(
        [FromQuery] GetHospitalAdminDashboardRequest request,
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
