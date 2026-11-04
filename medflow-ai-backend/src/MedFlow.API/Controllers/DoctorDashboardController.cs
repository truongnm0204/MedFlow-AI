using System.Security.Claims;
using MedFlow.Application.DTOs.DoctorDashboard;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/doctor/dashboard")]
[Route("api/v1/doctors/me/dashboard")]
[Route("api/v1/clinical/doctor-dashboard")]
[Authorize(Roles = "DOCTOR")]
public sealed class DoctorDashboardController : ControllerBase
{
    private readonly IDoctorDashboardService _dashboardService;

    public DoctorDashboardController(IDoctorDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// GET /api/v1/doctor/dashboard/summary — Lấy thông tin tổng quan bàn khám Bác sĩ (Doctor Dashboard).
    /// </summary>
    [HttpGet]
    [HttpGet("summary")]
    [ProducesResponseType<DoctorDashboardResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DoctorDashboardResponseDto>> GetDashboard(
        [FromQuery] GetDoctorDashboardRequest request,
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
