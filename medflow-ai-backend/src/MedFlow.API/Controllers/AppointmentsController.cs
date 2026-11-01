using System.Security.Claims;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Services.Appointments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/appointments")]
[Authorize(Roles = "PATIENT")]
public sealed class AppointmentsController : ControllerBase
{
    private readonly IAppointmentSlotService _slotService;

    public AppointmentsController(IAppointmentSlotService slotService)
    {
        _slotService = slotService;
    }

    /// <summary>GET /api/v1/appointments/available-slots — Xem ngày/ca/slot còn khả dụng theo bác sĩ và dịch vụ khám.</summary>
    [HttpGet("available-slots")]
    [ProducesResponseType<AvailableSlotsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AvailableSlotsResponse>> GetAvailableSlots(
        [FromQuery] GetAvailableSlotsRequest request,
        CancellationToken ct)
        => Ok(await _slotService.GetAvailableSlotsAsync(GetUserId(), request, ct));

    /// <summary>GET /api/v1/appointments/service-shifts — Xem ngày và ca khám khả dụng theo dịch vụ khám.</summary>
    [HttpGet("service-shifts")]
    [ProducesResponseType<ServiceShiftsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceShiftsResponse>> GetServiceShifts(
        [FromQuery] GetServiceShiftsRequest request,
        CancellationToken ct)
        => Ok(await _slotService.GetServiceShiftsAsync(GetUserId(), request, ct));

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
            throw new UnauthorizedAccessException("Token không hợp lệ");

        return userId;
    }
}
