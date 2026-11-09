using System.Globalization;
using System.Security.Claims;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class ReceptionCheckInController : ControllerBase
{
    private readonly ICheckInScheduledPatientService _checkInService;

    public ReceptionCheckInController(ICheckInScheduledPatientService checkInService)
    {
        _checkInService = checkInService;
    }

    /// <summary>
    /// POST /api/v1/reception/appointments/{idOrCode}/check-in — Tiếp nhận (check-in) bệnh nhân có lịch hẹn tại quầy tiếp đón (UC24).
    /// </summary>
    [HttpPost("api/v1/reception/appointments/{idOrCode}/check-in")]
    [HttpPost("api/v1/reception/appointments/{idOrCode}/checkin")]
    [HttpPost("api/v1/appointments/{idOrCode}/check-in")]
    [HttpPost("api/v1/reception/check-in/appointments/{idOrCode}")]
    [ProducesResponseType<CheckInScheduledPatientResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CheckInScheduledPatientResponseDto>> CheckIn(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CheckInScheduledPatientRequest? request = null,
        CancellationToken ct = default)
    {
        var result = await _checkInService.CheckInScheduledPatientAsync(
            GetUserId(),
            idOrCode,
            request ?? new CheckInScheduledPatientRequest(),
            GetIp(),
            ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/v1/reception/check-in — Tiếp nhận (check-in) bệnh nhân có lịch hẹn qua mã lịch hẹn trong payload (UC24).
    /// </summary>
    [HttpPost("api/v1/reception/check-in")]
    [HttpPost("api/v1/reception/visits/check-in")]
    [HttpPost("api/v1/visits/check-in")]
    [ProducesResponseType<CheckInScheduledPatientResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CheckInScheduledPatientResponseDto>> CheckInByBody(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CheckInScheduledPatientRequest? request = null,
        CancellationToken ct = default)
    {
        var idOrCode = request?.AppointmentId?.ToString(CultureInfo.InvariantCulture)
            ?? request?.AppointmentCode
            ?? string.Empty;

        var result = await _checkInService.CheckInScheduledPatientAsync(
            GetUserId(),
            idOrCode,
            request ?? new CheckInScheduledPatientRequest(),
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
