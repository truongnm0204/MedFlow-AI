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

    public PatientsController(IPatientProfileService profileService)
    {
        _profileService = profileService;
    }

    /// <summary>GET /api/v1/patients/me/profile — Xem thông tin hồ sơ cá nhân của bệnh nhân đang đăng nhập.</summary>
    [HttpGet("me/profile")]
    [ProducesResponseType<PatientProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientProfileResponse>> GetMyProfile(CancellationToken ct)
        => Ok(await _profileService.GetPersonalProfileAsync(GetUserId(), ct));

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
            throw new UnauthorizedAccessException("Token không hợp lệ");

        return userId;
    }
}
