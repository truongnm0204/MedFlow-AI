using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize]
public sealed class VisitQueueEnrollmentController : ControllerBase
{
    private readonly IVisitQueueEnrollmentService _enrollmentService;

    public VisitQueueEnrollmentController(IVisitQueueEnrollmentService enrollmentService)
    {
        _enrollmentService = enrollmentService;
    }

    /// <summary>
    /// GET /api/v1/visits/{idOrCode}/queue-enrollment-context — Xem ngữ cảnh điều kiện lấy số hàng đợi của lượt khám (Stage E2 / QE-04..20).
    /// </summary>
    [HttpGet("/api/v1/visits/{idOrCode}/queue-enrollment-context")]
    [ProducesResponseType<VisitQueueEnrollmentContextDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisitQueueEnrollmentContextDto>> GetEnrollmentContext(
        string idOrCode,
        CancellationToken ct = default)
        => Ok(await _enrollmentService.GetEnrollmentContextAsync(GetUserId(), idOrCode, ct));

    /// <summary>
    /// POST /api/v1/visits/{idOrCode}/queue-enrollments — Đăng ký lấy số vào 1 hàng đợi đích (DIAGNOSTIC / PHARMACY / RETURN_CONSULTATION) (Stage E2 / QE-04..20).
    /// </summary>
    [HttpPost("/api/v1/visits/{idOrCode}/queue-enrollments")]
    [ProducesResponseType<VisitQueueEnrollmentTicketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitQueueEnrollmentTicketDto>> CreateEnrollment(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateVisitQueueEnrollmentRequestDto? request = null,
        CancellationToken ct = default)
        => Ok(await _enrollmentService.EnrollAsync(GetUserId(), idOrCode, request!, GetIp(), ct));

    /// <summary>
    /// POST /api/v1/queue-entries/{idOrCode}/leave — Chủ động rời hàng đợi đang chờ mà không hủy chỉ định hoặc hoàn phí (Stage E2 / QE-18).
    /// </summary>
    [HttpPost("/api/v1/queue-entries/{idOrCode}/leave")]
    [ProducesResponseType<LeaveQueueEntryResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaveQueueEntryResponseDto>> LeaveQueueEntry(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LeaveQueueEntryRequestDto? request = null,
        CancellationToken ct = default)
        => Ok(await _enrollmentService.LeaveQueueAsync(GetUserId(), idOrCode, request, GetIp(), ct));

    /// <summary>
    /// POST /api/v1/pharmacy/queue-entries/{idOrCode}/call — Nhân viên nhà thuốc gọi bệnh nhân tiếp nhận thuốc tại quầy (Stage E2 / PH-01..02).
    /// </summary>
    [Authorize(Roles = RoleNames.PharmacyStaff)]
    [HttpPost("/api/v1/pharmacy/queue-entries/{idOrCode}/call")]
    [ProducesResponseType<VisitQueueEnrollmentTicketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitQueueEnrollmentTicketDto>> CallPharmacyQueueEntry(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PharmacyQueueActionRequestDto? request = null,
        CancellationToken ct = default)
        => Ok(await _enrollmentService.CallPharmacyQueueEntryAsync(GetUserId(), idOrCode, request, GetIp(), ct));

    /// <summary>
    /// POST /api/v1/pharmacy/queue-entries/{idOrCode}/status — Nhân viên nhà thuốc cập nhật trạng thái hàng đợi quầy thuốc (Stage E2 / PH-01..02).
    /// </summary>
    [Authorize(Roles = RoleNames.PharmacyStaff)]
    [HttpPost("/api/v1/pharmacy/queue-entries/{idOrCode}/status")]
    [ProducesResponseType<VisitQueueEnrollmentTicketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitQueueEnrollmentTicketDto>> UpdatePharmacyQueueStatus(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PharmacyQueueActionRequestDto? request = null,
        CancellationToken ct = default)
        => Ok(await _enrollmentService.UpdatePharmacyQueueEntryStatusAsync(GetUserId(), idOrCode, request!, GetIp(), ct));

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
