using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "RECEPTION,DOCTOR,DIAGNOSTIC_STAFF,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class QueueStatusController : ControllerBase
{
    private readonly IQueueStatusService _queueStatusService;

    public QueueStatusController(IQueueStatusService queueStatusService)
    {
        _queueStatusService = queueStatusService;
    }

    /// <summary>GET /api/v1/queue-entries/{idOrCode}/status — Lấy trạng thái hiện tại, các trạng thái đích hợp lệ và lịch sử chuyển trạng thái của lượt xếp hàng khám.</summary>
    [HttpGet("/api/v1/queue-entries/{idOrCode}/status")]
    [HttpGet("/api/v1/reception/queue-entries/{idOrCode}/status")]
    [HttpGet("/api/v1/reception/examination-queue/entries/{idOrCode}/status")]
    [HttpGet("/api/v1/visits/{idOrCode}/queue-status")]
    [HttpGet("/api/v1/reception/visits/{idOrCode}/queue-status")]
    [ProducesResponseType<QueueEntryStatusDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QueueEntryStatusDetailDto>> GetQueueStatus(
        [FromRoute] string idOrCode,
        CancellationToken ct)
    {
        var result = await _queueStatusService.GetQueueEntryStatusAsync(GetUserId(), idOrCode, ct);
        return Ok(result);
    }

    /// <summary>PATCH /api/v1/queue-entries/{idOrCode}/status — Cập nhật trạng thái hàng đợi khám với kiểm tra máy trạng thái, đồng bộ lượt khám và ghi nhận nhật ký kiểm toán.</summary>
    [HttpPut("/api/v1/queue-entries/{idOrCode}/status")]
    [HttpPatch("/api/v1/queue-entries/{idOrCode}/status")]
    [HttpPost("/api/v1/queue-entries/{idOrCode}/status")]
    [HttpPut("/api/v1/reception/queue-entries/{idOrCode}/status")]
    [HttpPatch("/api/v1/reception/queue-entries/{idOrCode}/status")]
    [HttpPost("/api/v1/reception/queue-entries/{idOrCode}/status")]
    [HttpPut("/api/v1/reception/examination-queue/entries/{idOrCode}/status")]
    [HttpPatch("/api/v1/reception/examination-queue/entries/{idOrCode}/status")]
    [HttpPost("/api/v1/reception/examination-queue/entries/{idOrCode}/status")]
    [HttpPut("/api/v1/visits/{idOrCode}/queue-status")]
    [HttpPatch("/api/v1/visits/{idOrCode}/queue-status")]
    [HttpPost("/api/v1/visits/{idOrCode}/queue-status")]
    [HttpPut("/api/v1/reception/visits/{idOrCode}/queue-status")]
    [HttpPatch("/api/v1/reception/visits/{idOrCode}/queue-status")]
    [HttpPost("/api/v1/reception/visits/{idOrCode}/queue-status")]
    [ProducesResponseType<UpdateQueueStatusResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdateQueueStatusResponseDto>> UpdateQueueStatus(
        [FromRoute] string idOrCode,
        [FromBody] UpdateQueueStatusRequest request,
        CancellationToken ct)
    {
        var result = await _queueStatusService.UpdateQueueEntryStatusAsync(
            GetUserId(),
            idOrCode,
            request,
            GetIp(),
            ct);
        return Ok(result);
    }

    /// <summary>PATCH /api/v1/queue-entries/{idOrCode}/priority — Áp dụng/thay đổi mức ưu tiên ngoại trú (NORMAL/ELDERLY/PREGNANT) có lý do, xác minh theo chính sách, tăng state_version và ghi audit.</summary>
    [HttpPatch("/api/v1/queue-entries/{idOrCode}/priority")]
    [HttpPatch("/api/v1/reception/queue-entries/{idOrCode}/priority")]
    [HttpPatch("/api/v1/reception/examination-queue/entries/{idOrCode}/priority")]
    [Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
    [ProducesResponseType<UpdateQueuePriorityResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdateQueuePriorityResponseDto>> UpdateQueuePriority(
        [FromRoute] string idOrCode,
        [FromBody] UpdateQueuePriorityRequest request,
        CancellationToken ct)
        => Ok(await _queueStatusService.UpdateQueueEntryPriorityAsync(GetUserId(), idOrCode, request, GetIp(), ct));

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
