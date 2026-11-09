using System.Security.Claims;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class ReceptionPatientDetailController : ControllerBase
{
    private readonly IReceptionPatientDetailService _patientDetailService;

    public ReceptionPatientDetailController(IReceptionPatientDetailService patientDetailService)
    {
        _patientDetailService = patientDetailService;
    }

    /// <summary>
    /// GET /api/v1/reception/patients/{idOrCode} — Xem chi tiết hồ sơ bệnh nhân tại quầy tiếp đón (UC22 / M2-014).
    /// </summary>
    [HttpGet("api/v1/reception/patients/{idOrCode}")]
    [HttpGet("api/v1/reception/patients/{idOrCode}/detail")]
    [HttpGet("api/v1/patients/{idOrCode}/reception-detail")]
    [ProducesResponseType<PatientDetailResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PatientDetailResponseDto>> GetPatientDetail(
        [FromRoute] string idOrCode,
        CancellationToken ct = default)
    {
        var result = await _patientDetailService.GetPatientDetailAsync(
            GetUserId(),
            idOrCode,
            ct);
        return Ok(result);
    }

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
}
