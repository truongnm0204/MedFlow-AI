using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/reception/visits")]
[Route("api/v1/visits")]
[Authorize(Roles = "RECEPTION")]
public sealed class ReceptionVisitsController : ControllerBase
{
    private readonly IVisitTicketService _visitTicketService;

    public ReceptionVisitsController(IVisitTicketService visitTicketService)
    {
        _visitTicketService = visitTicketService;
    }

    /// <summary>
    /// GET /api/v1/reception/visits/{idOrCode}/ticket — Lấy thông tin phiếu khám bệnh ngoại trú (UC29).
    /// </summary>
    [HttpGet("{idOrCode}/ticket")]
    [HttpGet("{idOrCode}/examination-ticket")]
    [ProducesResponseType<ExaminationTicketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExaminationTicketDto>> GetExaminationTicket(
        [FromRoute] string idOrCode,
        CancellationToken ct)
    {
        var result = await _visitTicketService.GetExaminationTicketAsync(GetUserId(), idOrCode, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/v1/reception/visits/{idOrCode}/ticket/print — Thực hiện in phiếu khám bệnh ngoại trú và ghi nhận nhật ký kiểm toán (UC29).
    /// </summary>
    [HttpPost("{idOrCode}/ticket/print")]
    [HttpPost("{idOrCode}/ticket")]
    [HttpPost("{idOrCode}/examination-ticket/print")]
    [HttpPost("{idOrCode}/examination-ticket")]
    [ProducesResponseType<ExaminationTicketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExaminationTicketDto>> PrintExaminationTicket(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PrintExaminationTicketRequest? request = null,
        CancellationToken ct = default)
    {
        var result = await _visitTicketService.PrintExaminationTicketAsync(
            GetUserId(),
            idOrCode,
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
