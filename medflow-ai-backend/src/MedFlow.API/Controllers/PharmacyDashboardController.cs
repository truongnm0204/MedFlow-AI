using System.Security.Claims;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/pharmacy/dashboard")]
[Route("api/v1/pharmacy-dashboard")]
[Route("api/v1/staff/pharmacy-dashboard")]
[Authorize(Roles = "PHARMACY_STAFF")]
public sealed class PharmacyDashboardController : ControllerBase
{
    private readonly IPharmacyDashboardService _dashboardService;

    public PharmacyDashboardController(IPharmacyDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// GET /api/v1/pharmacy/dashboard/summary — Lấy thông tin tổng quan bảng điều khiển Nhà thuốc (Pharmacy Dashboard).
    /// </summary>
    [HttpGet]
    [HttpGet("summary")]
    [ProducesResponseType<PharmacyDashboardResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PharmacyDashboardResponseDto>> GetDashboard(
        [FromQuery] GetPharmacyDashboardRequest request,
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
