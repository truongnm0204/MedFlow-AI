using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class ExaminationQueueController : ControllerBase
{
    private readonly IExaminationQueueService _examinationQueueService;

    public ExaminationQueueController(IExaminationQueueService examinationQueueService)
    {
        _examinationQueueService = examinationQueueService;
    }

    /// <summary>
    /// GET /api/v1/reception/examination-queue — Hiển thị hàng đợi khám bệnh theo điểm dịch vụ / buồng khám cùng trạng thái và thứ tự phục vụ hiện tại (UC28).
    /// </summary>
    [HttpGet("/api/v1/reception/examination-queue")]
    [HttpGet("/api/v1/examination-queue")]
    [HttpGet("/api/v1/queues/examination")]
    [HttpGet("/api/v1/reception/queues/examination")]
    [ProducesResponseType<ExaminationQueueResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExaminationQueueResponseDto>> GetExaminationQueue(
        [FromQuery] GetExaminationQueueRequest request,
        CancellationToken ct)
    {
        var result = await _examinationQueueService.GetExaminationQueueAsync(GetUserId(), request, ct);
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
