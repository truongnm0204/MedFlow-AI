using System.Security.Claims;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/diagnostic")]
[Authorize(Roles = "DIAGNOSTIC_STAFF")]
public sealed class DiagnosticWorklistController : ControllerBase
{
    private readonly IDiagnosticWorklistService _worklistService;

    public DiagnosticWorklistController(IDiagnosticWorklistService worklistService)
    {
        _worklistService = worklistService;
    }

    /// <summary>
    /// GET /api/v1/diagnostic/worklist — Xem danh sách công việc và phiếu chỉ định Cận lâm sàng (UC39 — View Diagnostic Worklist).
    /// </summary>
    [HttpGet("worklist")]
    [HttpGet("orders")]
    [HttpGet("orders/worklist")]
    [HttpGet("/api/v1/diagnostic-worklist")]
    [ProducesResponseType<DiagnosticWorklistResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticWorklistResponseDto>> GetWorklist(
        [FromQuery] GetDiagnosticWorklistRequest request,
        CancellationToken ct)
    {
        var result = await _worklistService.GetWorklistAsync(GetUserId(), request, ct);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/v1/diagnostic/worklist/{orderIdOrCode} — Xem chi tiết một phiếu chỉ định Cận lâm sàng trong danh sách công việc (UC39).
    /// </summary>
    [HttpGet("worklist/{orderIdOrCode}")]
    [HttpGet("orders/{orderIdOrCode:regex(^(?!worklist$|review$).+)}")]
    [HttpGet("/api/v1/diagnostic-worklist/{orderIdOrCode}")]
    [ProducesResponseType<DiagnosticWorklistItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticWorklistItemDto>> GetWorklistOrderDetail(
        [FromRoute] string orderIdOrCode,
        CancellationToken ct)
    {
        var result = await _worklistService.GetWorklistOrderDetailAsync(GetUserId(), orderIdOrCode, ct);
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
