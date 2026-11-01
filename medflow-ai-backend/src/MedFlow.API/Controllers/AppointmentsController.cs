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
    private readonly IAppointmentBookingService _bookingService;

    public AppointmentsController(
        IAppointmentSlotService slotService,
        IAppointmentBookingService bookingService)
    {
        _slotService = slotService;
        _bookingService = bookingService;
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

    /// <summary>POST /api/v1/appointments — Đặt lịch khám (UC12).</summary>
    [HttpPost]
    [ProducesResponseType<CreateAppointmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateAppointmentResponse>> Create(
        [FromBody] CreateAppointmentRequest request,
        CancellationToken ct)
    {
        var result = await _bookingService.CreateAsync(GetUserId(), request, GetIp(), ct);
        return Created($"/api/v1/appointments/{result.AppointmentId}", result);
    }

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
            throw new UnauthorizedAccessException("Token không hợp lệ");

        return userId;
    }

    private string? GetIp()
        => Request.Headers.TryGetValue("X-Forwarded-For", out var ff)
            ? ff.ToString().Split(',')[0].Trim()
            : HttpContext.Connection.RemoteIpAddress?.ToString();
}
