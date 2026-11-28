using System.Security.Claims;
using MedFlow.Application.DTOs.Ekyc;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

/// <summary>
/// Reception-assisted identity work (owner scope 2026-11-28): staff drives the SAME VNPT eKYC
/// engine for a patient profile. [Authorize(Roles)] is layer 1; the service layer additionally
/// enforces PATIENT_IDENTITY_VERIFY + staff status. Staff can never set VERIFIED directly —
/// only a successful finalize inside the engine does.
/// </summary>
[ApiController]
[Route("api/v1/reception/ekyc")]
[Authorize(Roles = $"{RoleNames.Reception},{RoleNames.HospitalAdmin},{RoleNames.SystemAdmin}")]
public sealed class EkycReceptionController : ControllerBase
{
    private readonly IEkycSessionService _sessionService;
    private readonly IReceptionPatientProfileService _profileService;

    public EkycReceptionController(
        IEkycSessionService sessionService,
        IReceptionPatientProfileService profileService)
    {
        _sessionService = sessionService;
        _profileService = profileService;
    }

    /// <summary>
    /// POST /api/v1/reception/ekyc/patients/{patientId}/sessions — bắt đầu eKYC cho hồ sơ bệnh nhân.
    /// Hồ sơ phải tồn tại, ACTIVE và chưa VERIFIED; performed_by = lễ tân hiện tại.
    /// </summary>
    [HttpPost("patients/{patientId:long}/sessions")]
    [ProducesResponseType<CreateEkycSessionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CreateEkycSessionResponse>> CreatePatientSession(
        [FromRoute] long patientId,
        [FromBody] CreateReceptionEkycSessionRequest request,
        CancellationToken ct)
    {
        var effective = request with { PatientId = patientId };
        var response = await _sessionService.CreateReceptionAsync(
            GetUserId(), effective, GetIp(), GetUserAgent(), ct);
        return Created($"/api/v1/ekyc/sessions/{response.SessionId}", response);
    }

    /// <summary>
    /// POST /api/v1/reception/ekyc/patients/unverified — tạo hồ sơ bệnh nhân KHÔNG xác minh
    /// (ngoại lệ: trẻ em, người nước ngoài, không giấy tờ, không đồng ý eKYC, VNPT unavailable,
    /// hết quota). Bắt buộc lý do; dedupe chạy trước; audit đầy đủ. Không bao giờ set VERIFIED.
    /// </summary>
    [HttpPost("patients/unverified")]
    [ProducesResponseType<CreateUnverifiedPatientProfileResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateUnverifiedPatientProfileResponse>> CreateUnverifiedProfile(
        [FromBody] CreateUnverifiedPatientProfileRequest request,
        CancellationToken ct)
    {
        var result = await _profileService.CreateUnverifiedAsync(GetUserId(), request, GetIp(), ct);
        return Created($"/api/v1/reception/patients/{result.PatientId}", result);
    }

    /// <summary>GET /api/v1/reception/ekyc/sessions/{id} — trạng thái phiên do lễ tân điều khiển.</summary>
    [HttpGet("sessions/{id:long}")]
    [ProducesResponseType<EkycSessionStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EkycSessionStatusResponse>> GetSession(long id, CancellationToken ct)
        => Ok(await _sessionService.GetStatusAsync(GetUserId(), id, ct));

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
        {
            throw new UnauthorizedAccessException("Token không hợp lệ");
        }

        return userId;
    }

    private string? GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? GetUserAgent() => Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
}
