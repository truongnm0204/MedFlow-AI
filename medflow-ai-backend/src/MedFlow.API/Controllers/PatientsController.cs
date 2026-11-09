using System.Security.Claims;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "PATIENT")]
public sealed class PatientsController : ControllerBase
{
    private readonly IPatientProfileService _profileService;
    private readonly IFamilyProfileService _familyProfileService;

    public PatientsController(
        IPatientProfileService profileService,
        IFamilyProfileService familyProfileService)
    {
        _profileService = profileService;
        _familyProfileService = familyProfileService;
    }

    /// <summary>GET /api/v1/patients/me/profile — Xem thông tin hồ sơ cá nhân của bệnh nhân đang đăng nhập.</summary>
    [HttpGet("me/profile")]
    [ProducesResponseType<PatientProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientProfileResponse>> GetMyProfile(CancellationToken ct)
        => Ok(await _profileService.GetPersonalProfileAsync(GetUserId(), ct));

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

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
            throw new UnauthorizedAccessException("Token không hợp lệ");

        return userId;
    }
}
