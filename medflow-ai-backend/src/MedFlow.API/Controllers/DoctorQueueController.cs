using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/doctor/assigned-queue")]
[Authorize(Roles = "DOCTOR")]
public sealed class DoctorQueueController : ControllerBase
{
    private readonly IExaminationQueueService _examinationQueueService;

    public DoctorQueueController(IExaminationQueueService examinationQueueService)
    {
        _examinationQueueService = examinationQueueService;
    }

    /// <summary>
    /// GET /api/v1/doctor/assigned-queue — Hàng đợi được phân công của Bác sĩ (UC30).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<DoctorAssignedQueueResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DoctorAssignedQueueResponseDto>> GetAssignedQueue(
        [FromQuery] GetDoctorAssignedQueueRequest request,
        CancellationToken ct)
    {
        var result = await _examinationQueueService.GetDoctorAssignedQueueAsync(GetUserId(), request, ct);
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
