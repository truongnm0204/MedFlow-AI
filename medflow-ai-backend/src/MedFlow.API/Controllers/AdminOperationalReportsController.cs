using System.Security.Claims;
using MedFlow.Application.DTOs.Reports;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/reports/operational")]
[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class AdminOperationalReportsController(
    IOperationalReportService reportService) : ControllerBase
{
    /// <summary>
    /// GET /api/v1/admin/reports/operational — Báo cáo vận hành theo khoảng thời gian, khoa và dịch vụ (UC56 / ADR-0049).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<OperationalReportResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OperationalReportResponseDto>> GetOperationalReport(
        [FromQuery] GetOperationalReportRequest request,
        CancellationToken ct)
    {
        var result = await reportService.GetOperationalReportAsync(GetUserId(), request, ct);
        return Ok(result);
    }

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
        {
            throw new UnauthorizedAccessException("Token không hợp lệ");
        }

        return userId;
    }
}
