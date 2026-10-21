using System.Security.Claims;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/patients")]
[Authorize] // Bắt buộc đăng nhập
public sealed class PatientsController : ControllerBase
{
    private readonly IPatientProfileService _profileService;

    public PatientsController(IPatientProfileService profileService)
    {
        _profileService = profileService;
    }

    /// <summary>PUT /api/v1/patients/me/profile — Cập nhật thông tin hồ sơ cá nhân của bệnh nhân đăng nhập.</summary>
    [HttpPut("me/profile")]
    [ProducesResponseType<PatientProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientProfileResponse>> UpdateMyProfile(
        [FromBody] UpdatePersonalProfileRequest request, CancellationToken ct)
    {
        // Lấy UserId từ Claims của JWT Token
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var result = await _profileService.UpdatePersonalProfileAsync(userId, request, ct);
        return Ok(result);
    }
}
