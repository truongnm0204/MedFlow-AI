using System.Security.Claims;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = RoleNames.Patient)]
public sealed class PatientsController : ControllerBase
{
    private readonly IPatientProfileService _profileService;
    private readonly IFamilyProfileService _familyProfileService;
    private readonly IPatientDiagnosticResultService _diagnosticResultService;
    private readonly IPatientPrescriptionService _prescriptionService;
    private readonly IPatientIdentityClaimService _identityClaimService;
    private readonly IPatientVisitListService _visitListService;
    private readonly IPatientMedicalRecordService _medicalRecordService;

    public PatientsController(
        IPatientProfileService profileService,
        IFamilyProfileService familyProfileService,
        IPatientDiagnosticResultService diagnosticResultService,
        IPatientIdentityClaimService identityClaimService,
        IPatientPrescriptionService prescriptionService,
        IPatientVisitListService visitListService,
        IPatientMedicalRecordService medicalRecordService)
    {
        _profileService = profileService;
        _familyProfileService = familyProfileService;
        _diagnosticResultService = diagnosticResultService;
        _identityClaimService = identityClaimService;
        _prescriptionService = prescriptionService;
        _visitListService = visitListService;
        _medicalRecordService = medicalRecordService;
    }

    /// <summary>GET /api/v1/patients/me/profile — Xem thông tin hồ sơ cá nhân của bệnh nhân đang đăng nhập.</summary>
    [HttpGet("me/profile")]
    [ProducesResponseType<PatientProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientProfileResponse>> GetMyProfile(CancellationToken ct)
        => Ok(await _profileService.GetPersonalProfileAsync(GetUserId(), ct));

    /// <summary>PUT/PATCH /api/v1/patients/me/profile — Cập nhật thông tin liên hệ và y tế cá nhân của bệnh nhân đang đăng nhập (UC05).</summary>
    [HttpPut("me/profile")]
    [HttpPatch("me/profile")]
    [ProducesResponseType<PatientProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientProfileResponse>> UpdateMyProfile(
        [FromBody] UpdateMyPatientProfileRequest request,
        CancellationToken ct)
        => Ok(await _profileService.UpdatePersonalProfileAsync(
            GetUserId(),
            request,
            isPatch: HttpMethods.IsPatch(Request.Method),
            ipAddress: GetIp(),
            ct: ct));

    /// <summary>GET /api/v1/patients/me/visits — Danh sách lượt khám thuộc quyền xem của bệnh nhân (UC18 / #778).</summary>
    [HttpGet("me/visits")]
    [ProducesResponseType<PatientVisitListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PatientVisitListResponseDto>> GetMyVisits(
        [FromQuery] int? limit = null,
        CancellationToken ct = default)
        => Ok(await _visitListService.GetMyVisitsAsync(GetUserId(), limit, ct));
    /// <summary>POST /api/v1/patients/me/identity-claims/otp — Gửi OTP step-up cho yêu cầu liên kết hồ sơ.</summary>
    [HttpPost("me/identity-claims/otp")]
    [EnableRateLimiting("otp-resend")]
    [ProducesResponseType<SendIdentityClaimOtpResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SendIdentityClaimOtpResponse>> SendIdentityClaimOtp(CancellationToken ct)
        => Ok(await _identityClaimService.SendOtpAsync(GetUserId(), ct));

    /// <summary>POST /api/v1/patients/me/identity-claims — Gửi yêu cầu liên kết hồ sơ đã có.</summary>
    [HttpPost("me/identity-claims")]
    [EnableRateLimiting("otp-verify")]
    [ProducesResponseType<IdentityClaimResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IdentityClaimResponse>> CreateIdentityClaim(
        [FromBody] CreateIdentityClaimRequest request,
        CancellationToken ct)
    {
        var result = await _identityClaimService.CreateAsync(GetUserId(), request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>GET /api/v1/patients/me/identity-claims — Danh sách yêu cầu liên kết của tôi.</summary>
    [HttpGet("me/identity-claims")]
    [ProducesResponseType<IReadOnlyList<IdentityClaimResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<IdentityClaimResponse>>> GetMyIdentityClaims(
        CancellationToken ct)
        => Ok(await _identityClaimService.GetMineAsync(GetUserId(), ct));

    /// <summary>GET /api/v1/patients/me/identity-claims/{id} — Trạng thái một yêu cầu liên kết.</summary>
    [HttpGet("me/identity-claims/{id:long}")]
    [ProducesResponseType<IdentityClaimResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IdentityClaimResponse>> GetMyIdentityClaim(
        [FromRoute] long id,
        CancellationToken ct)
        => Ok(await _identityClaimService.GetMineByIdAsync(GetUserId(), id, ct));

    /// <summary>GET /api/v1/patients/me/family-members — Xem danh sách hồ sơ người thân của bệnh nhân đang đăng nhập.</summary>
    [HttpGet("me/family-members")]
    [HttpGet("me/family-profiles")]
    [ProducesResponseType<IReadOnlyList<FamilyMemberResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<FamilyMemberResponse>>> GetMyFamilyMembers(
        [FromQuery] bool includeSelf = true,
        CancellationToken ct = default)
        => Ok(await _familyProfileService.GetFamilyMembersAsync(GetUserId(), includeSelf, ct));

    /// <summary>GET /api/v1/patients/me/family-members/{identifier} — Xem chi tiết hồ sơ một người thân theo ID hoặc mã.</summary>
    [HttpGet("me/family-members/{identifier}")]
    [HttpGet("me/family-profiles/{identifier}")]
    [ProducesResponseType<FamilyMemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FamilyMemberResponse>> GetMyFamilyMemberDetail(
        [FromRoute] string identifier,
        CancellationToken ct = default)
        => Ok(await _familyProfileService.GetFamilyMemberDetailAsync(GetUserId(), identifier, ct));

    /// <summary>POST /api/v1/patients/me/family-members — Tạo hồ sơ người thân mới liên kết với bệnh nhân.</summary>
    [HttpPost("me/family-members")]
    [HttpPost("me/family-profiles")]
    [ProducesResponseType<FamilyMemberResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FamilyMemberResponse>> CreateFamilyMember(
        [FromBody] CreateFamilyMemberRequest request,
        CancellationToken ct = default)
    {
        var result = await _familyProfileService.CreateFamilyMemberAsync(GetUserId(), request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>PUT /api/v1/patients/me/family-members/{identifier} — Cập nhật thông tin hồ sơ người thân đã liên kết.</summary>
    [HttpPut("me/family-members/{identifier}")]
    [HttpPut("me/family-profiles/{identifier}")]
    [ProducesResponseType<FamilyMemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FamilyMemberResponse>> UpdateFamilyMember(
        [FromRoute] string identifier,
        [FromBody] UpdateFamilyMemberRequest request,
        CancellationToken ct = default)
    {
        var result = await _familyProfileService.UpdateFamilyMemberAsync(GetUserId(), identifier, request, ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/patients/me/diagnostic-results — Xem các kết quả cận lâm sàng được công bố cho bệnh nhân đang đăng nhập.</summary>
    [HttpGet("me/diagnostic-results")]
    [ProducesResponseType<PatientDiagnosticResultListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientDiagnosticResultListResponseDto>> GetMyDiagnosticResults(
        [FromQuery] GetPatientDiagnosticResultsRequest? request,
        CancellationToken ct)
        => Ok(await _diagnosticResultService.GetMyResultsAsync(GetUserId(), request, ct, GetIp()));

    /// <summary>GET /api/v1/patients/me/diagnostic-results/{idOrCode} — Xem chi tiết kết quả cận lâm sàng theo mã chỉ định.</summary>
    [HttpGet("me/diagnostic-results/{idOrCode}")]
    [ProducesResponseType<PatientDiagnosticResultDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientDiagnosticResultDetailDto>> GetMyDiagnosticResult(
        string idOrCode,
        CancellationToken ct)
        => Ok(await _diagnosticResultService.GetMyResultAsync(GetUserId(), idOrCode, ct, GetIp()));

    /// <summary>GET /api/v1/patients/me/prescriptions — Xem danh sách đơn thuốc của bệnh nhân đang đăng nhập.</summary>
    [HttpGet("me/prescriptions")]
    [ProducesResponseType<PatientPrescriptionListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientPrescriptionListResponseDto>> GetMyPrescriptions(
        [FromQuery] GetPatientPrescriptionsRequest? request,
        CancellationToken ct)
        => Ok(await _prescriptionService.GetMyPrescriptionsAsync(GetUserId(), request, ct, GetIp()));

    /// <summary>GET /api/v1/patients/me/prescriptions/{idOrCode} — Xem thuốc và hướng dẫn sử dụng của một đơn thuốc.</summary>
    [HttpGet("me/prescriptions/{idOrCode}")]
    [ProducesResponseType<PatientPrescriptionDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientPrescriptionDetailDto>> GetMyPrescription(
        string idOrCode,
        CancellationToken ct)
        => Ok(await _prescriptionService.GetMyPrescriptionAsync(GetUserId(), idOrCode, ct, GetIp()));

    /// <summary>GET /api/v1/patients/me/medical-records — Xem danh sách hồ sơ bệnh án điện tử đã hoàn tất của bệnh nhân và người thân được xác minh PHI.</summary>
    [HttpGet("me/medical-records")]
    [ProducesResponseType<PatientMedicalRecordListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientMedicalRecordListResponseDto>> GetMyMedicalRecords(
        [FromQuery] GetPatientMedicalRecordsRequest? request,
        CancellationToken ct)
        => Ok(await _medicalRecordService.GetMyMedicalRecordsAsync(GetUserId(), request, ct, GetIp()));

    /// <summary>GET /api/v1/patients/me/medical-records/{idOrCode} — Xem chi tiết hồ sơ bệnh án điện tử theo ID bệnh án, mã EMR hoặc mã lượt khám.</summary>
    [HttpGet("me/medical-records/{idOrCode}")]
    [ProducesResponseType<PatientMedicalRecordDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientMedicalRecordDetailDto>> GetMyMedicalRecord(
        string idOrCode,
        CancellationToken ct)
        => Ok(await _medicalRecordService.GetMyMedicalRecordAsync(GetUserId(), idOrCode, ct, GetIp()));

    /// <summary>DELETE /api/v1/patients/me/family-members/{identifier} — Gỡ liên kết hồ sơ người thân.</summary>
    [HttpDelete("me/family-members/{identifier}")]
    [HttpDelete("me/family-profiles/{identifier}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFamilyMember(
        [FromRoute] string identifier,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RemoveFamilyMemberRequest? request = null,
        [FromQuery] string? reason = null,
        CancellationToken ct = default)
    {
        var finalReason = request?.Reason ?? reason;
        await _familyProfileService.RemoveFamilyMemberAsync(GetUserId(), identifier, finalReason, ct);
        return NoContent();
    }

    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
            throw new UnauthorizedAccessException("Token không hợp lệ");

        return userId;
    }
}
