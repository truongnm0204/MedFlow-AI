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

    /// <summary>
    /// POST /api/v1/reception/queue-entries/{idOrCode}/early-arrival-backfill — Điều phối bệnh nhân đến sớm / Safe Backfill hoặc giữ chờ khung giờ hẹn (UC28 / M2-044).
    /// </summary>
    [HttpPost("/api/v1/reception/queue-entries/{idOrCode}/early-arrival-backfill")]
    [HttpPost("/api/v1/reception/examination-queue/entries/{idOrCode}/early-arrival-backfill")]
    [ProducesResponseType<EarlyArrivalBackfillResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EarlyArrivalBackfillResponseDto>> ApplyEarlyArrivalBackfill(
        string idOrCode,
        [FromBody] EarlyArrivalBackfillRequest request,
        CancellationToken ct)
        => Ok(await _examinationQueueService.ApplyEarlyArrivalBackfillAsync(GetUserId(), idOrCode, request, GetIp(), ct));

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
