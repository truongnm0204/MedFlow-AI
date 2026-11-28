using System.Security.Claims;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = $"{RoleNames.DiagnosticStaff},{RoleNames.Reception}")]
public sealed class DiagnosticOrderRoutingController : ControllerBase
{
    private readonly IDiagnosticOrderRoutingService _routingService;

    public DiagnosticOrderRoutingController(IDiagnosticOrderRoutingService routingService)
    {
        _routingService = routingService;
    }

    /// <summary>
    /// POST /api/v1/diagnostic/orders/{idOrCode}/reassign — Điều phối lại điểm CLS (#870).
    /// Bắt buộc lý do, ghi audit + lịch sử, cấm khi bệnh nhân đang thực hiện dịch vụ.
    /// </summary>
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/reassign")]
    [HttpPost("/api/v1/reception/diagnostic-orders/{idOrCode}/reassign")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode:regex(^(?!review$|receive$|execution$).+)}/reassign")]
    [ProducesResponseType<ReassignDiagnosticOrderResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReassignDiagnosticOrderResponseDto>> Reassign(
        string idOrCode,
        [FromBody] ReassignDiagnosticOrderRequest? request,
        CancellationToken ct)
        => Ok(await _routingService.ReassignAsync(GetUserId(), idOrCode, request, GetIp(), ct));

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
