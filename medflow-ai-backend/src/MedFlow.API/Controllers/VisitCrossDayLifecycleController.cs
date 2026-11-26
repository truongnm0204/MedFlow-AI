using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize]
public sealed class VisitCrossDayLifecycleController : ControllerBase
{
    private readonly IVisitCrossDayLifecycleService _service;

    public VisitCrossDayLifecycleController(IVisitCrossDayLifecycleService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/reception/visits/{idOrCode}/resume-context — Tra cứu ngữ cảnh tiếp tục lượt khám qua ngày cho quầy Tiếp đón / Bác sĩ / Quản trị.</summary>
    [HttpGet("/api/v1/reception/visits/{idOrCode}/resume-context")]
    [Authorize(Roles = $"{RoleNames.Reception},{RoleNames.Doctor},{RoleNames.DiagnosticStaff},{RoleNames.HospitalAdmin},{RoleNames.SystemAdmin}")]
    [ProducesResponseType<VisitResumeContextResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisitResumeContextResponseDto>> GetReceptionResumeContext(
        [FromRoute] string idOrCode,
        CancellationToken ct)
        => Ok(await _service.GetResumeContextAsync(GetUserId(), idOrCode, allowPatientRole: false, ct));

    /// <summary>GET /api/v1/visits/{idOrCode}/resume-context — Tra cứu ngữ cảnh lượt khám nhiều ngày (PATIENT chính chủ, DOCTOR, RECEPTION, HOSPITAL_ADMIN).</summary>
    [HttpGet("/api/v1/visits/{idOrCode}/resume-context")]
    [Authorize(Roles = $"{RoleNames.Patient},{RoleNames.Doctor},{RoleNames.Reception},{RoleNames.DiagnosticStaff},{RoleNames.HospitalAdmin},{RoleNames.SystemAdmin}")]
    [ProducesResponseType<VisitResumeContextResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisitResumeContextResponseDto>> GetVisitResumeContext(
        [FromRoute] string idOrCode,
        CancellationToken ct)
        => Ok(await _service.GetResumeContextAsync(GetUserId(), idOrCode, allowPatientRole: true, ct));

    /// <summary>POST /api/v1/reception/visits/{idOrCode}/resume — Xác nhận bệnh nhân có mặt và tiếp tục lượt khám qua ngày (DIAGNOSTIC / RETURN_CONSULTATION).</summary>
    [HttpPost("/api/v1/reception/visits/{idOrCode}/resume")]
    [HttpPost("/api/v1/visits/{idOrCode}/resume")]
    [Authorize(Roles = $"{RoleNames.Reception},{RoleNames.HospitalAdmin},{RoleNames.SystemAdmin}")]
    [ProducesResponseType<ResumeCrossDayVisitResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ResumeCrossDayVisitResponseDto>> ResumeVisit(
        [FromRoute] string idOrCode,
        [FromBody] ResumeCrossDayVisitRequestDto? request,
        CancellationToken ct)
        => Ok(await _service.ResumeVisitAsync(
            GetUserId(),
            idOrCode,
            request ?? new ResumeCrossDayVisitRequestDto(string.Empty),
            GetIp(),
            ct));

    /// <summary>POST /api/v1/reception/visits/{idOrCode}/defer — Ghi nhận bệnh nhân tạm hoãn thực hiện cận lâm sàng hoặc rời viện về nhà.</summary>
    [HttpPost("/api/v1/reception/visits/{idOrCode}/defer")]
    [HttpPost("/api/v1/visits/{idOrCode}/defer")]
    [Authorize(Roles = $"{RoleNames.Reception},{RoleNames.DiagnosticStaff},{RoleNames.Doctor},{RoleNames.HospitalAdmin},{RoleNames.SystemAdmin}")]
    [ProducesResponseType<DeferVisitOrOrdersResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeferVisitOrOrdersResponseDto>> DeferVisit(
        [FromRoute] string idOrCode,
        [FromBody] DeferVisitOrOrdersRequestDto? request,
        CancellationToken ct)
        => Ok(await _service.DeferVisitOrOrdersAsync(
            GetUserId(),
            idOrCode,
            request ?? new DeferVisitOrOrdersRequestDto(string.Empty),
            GetIp(),
            ct));

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
