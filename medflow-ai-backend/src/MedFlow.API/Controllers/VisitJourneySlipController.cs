using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize]
public sealed class VisitJourneySlipController : ControllerBase
{
    private readonly IJourneySlipService _journeySlipService;

    public VisitJourneySlipController(IJourneySlipService journeySlipService)
    {
        _journeySlipService = journeySlipService;
    }

    /// <summary>
    /// GET /api/v1/visits/{idOrCode}/journey-slip — Xem phiếu hành trình tối thiểu của lượt khám (Stage E2 / QE-21 / E2-R09).
    /// Chỉ bệnh nhân sở hữu lượt khám hoặc nhân sự cùng cơ sở y tế mới được xem.
    /// </summary>
    [HttpGet("/api/v1/visits/{idOrCode}/journey-slip")]
    [ProducesResponseType<JourneySlipDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JourneySlipDto>> GetJourneySlip(
        string idOrCode,
        CancellationToken ct = default)
        => Ok(await _journeySlipService.GetJourneySlipAsync(GetUserId(), idOrCode, ct));

    /// <summary>
    /// POST /api/v1/visits/{idOrCode}/journey-slip/print — Ghi nhận ý định in phiếu hành trình (Stage E2 / QE-21).
    /// Chỉ ghi audit log, không phát sinh tác vụ in vật lý.
    /// </summary>
    [HttpPost("/api/v1/visits/{idOrCode}/journey-slip/print")]
    [ProducesResponseType<JourneySlipPrintResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JourneySlipPrintResultDto>> PrintJourneySlip(
        string idOrCode,
        CancellationToken ct = default)
        => Ok(await _journeySlipService.PrintJourneySlipAsync(GetUserId(), idOrCode, GetIp(), ct));

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
