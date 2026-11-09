using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "DOCTOR,RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class QueueCallNextController : ControllerBase
{
    private readonly IExaminationQueueService _examinationQueueService;

    public QueueCallNextController(IExaminationQueueService examinationQueueService)
    {
        _examinationQueueService = examinationQueueService;
    }

    /// <summary>
    /// POST /api/v1/queue/service-points/{servicePointId}/call-next — Gọi bệnh nhân đủ điều kiện tiếp theo tại điểm phục vụ (UC31 / #94).
    /// </summary>
    [HttpPost("/api/v1/queue/service-points/{servicePointId}/call-next")]
    [ProducesResponseType<CallNextPatientResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CallNextPatientResponseDto>> CallNextPatient(
        string servicePointId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CallNextPatientRequest? request,
        CancellationToken ct)
        => Ok(await _examinationQueueService.CallNextPatientAsync(GetUserId(), servicePointId, request, GetIp(), ct));

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
