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
    private readonly IAppointmentListService _listService;
    private readonly IAppointmentDetailService _detailService;
    private readonly IAppointmentCancelService _cancelService;

    public AppointmentsController(
        IAppointmentSlotService slotService,
        IAppointmentBookingService bookingService,
        IAppointmentListService listService,
        IAppointmentDetailService detailService,
        IAppointmentCancelService cancelService)
    {
        _slotService = slotService;
        _bookingService = bookingService;
        _listService = listService;
        _detailService = detailService;
        _cancelService = cancelService;
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

    /// <summary>GET /api/v1/appointments/review-preview — Soát xét thông tin đặt lịch trước khi xác nhận (UC12 bước review).</summary>
    [HttpGet("review-preview")]
    [ProducesResponseType<AppointmentReviewPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentReviewPreviewResponse>> GetReviewPreview(
        [FromQuery] GetAppointmentReviewPreviewRequest request,
        CancellationToken ct)
        => Ok(await _bookingService.PreviewAsync(GetUserId(), request, ct));

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

    /// <summary>GET /api/v1/appointments/me — Xem danh sách lịch hẹn sắp tới và lịch sử của bệnh nhân và người thân.</summary>
    [HttpGet("me")]
    [ProducesResponseType<AppointmentListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AppointmentListResponse>> GetMyAppointments(
        [FromQuery] GetMyAppointmentsRequest request,
        CancellationToken ct)
        => Ok(await _listService.GetMyAppointmentsAsync(GetUserId(), request, ct));


    /// <summary>GET /api/v1/appointments/{idOrCode} — Xem chi tiết, trạng thái và hành động hợp lệ của một lịch hẹn (UC15).</summary>
    [HttpGet("{idOrCode}")]
    [ProducesResponseType<AppointmentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AppointmentDetailDto>> GetDetail(string idOrCode, CancellationToken ct)
        => Ok(await _detailService.GetMyAppointmentDetailAsync(GetUserId(), idOrCode, ct));

    /// <summary>POST /api/v1/appointments/{idOrCode}/cancel — Hủy lịch hẹn còn đủ điều kiện kèm lý do (UC14).</summary>
    [HttpPost("{idOrCode}/cancel")]
    [ProducesResponseType<AppointmentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentDetailDto>> Cancel(
        string idOrCode,
        [FromBody] CancelAppointmentRequest request,
        CancellationToken ct)
        => Ok(await _cancelService.CancelMyAppointmentAsync(GetUserId(), idOrCode, request, GetIp(), ct));

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
