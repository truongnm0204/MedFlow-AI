using System.Security.Claims;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/diagnostic/dashboard")]
[Route("api/v1/diagnostic-dashboard")]
[Route("api/v1/diagnostic/workbench")]
[Authorize(Roles = "DIAGNOSTIC_STAFF")]
public sealed class DiagnosticDashboardController : ControllerBase
{
    private readonly IDiagnosticDashboardService _dashboardService;

    public DiagnosticDashboardController(IDiagnosticDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// GET /api/v1/diagnostic/dashboard/summary — Lấy thông tin tổng quan bảng điều khiển Cận lâm sàng (Diagnostic Staff Dashboard).
    /// </summary>
    [HttpGet]
    [HttpGet("summary")]
    [ProducesResponseType<DiagnosticDashboardResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticDashboardResponseDto>> GetDashboard(
        [FromQuery] GetDiagnosticDashboardRequest request,
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
