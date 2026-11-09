using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "DOCTOR,RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class QueueReturnConsultationController : ControllerBase
{
    private readonly IExaminationQueueService _examinationQueueService;

    public QueueReturnConsultationController(IExaminationQueueService examinationQueueService)
    {
        _examinationQueueService = examinationQueueService;
    }

    /// <summary>
    /// POST /api/v1/reception/queue-entries/{idOrCode}/return-to-doctor — Tạo hoặc kích hoạt lượt khám lại đọc kết quả cận lâm sàng (RETURN_CONSULTATION) với kiểm tra đủ điều kiện kết quả bắt buộc và hiện diện (UC28 / M3-002 / #100).
    /// </summary>
    [HttpPost("/api/v1/reception/queue-entries/{idOrCode}/return-to-doctor")]
    [HttpPost("/api/v1/reception/examination-queue/entries/{idOrCode}/return-to-doctor")]
    [HttpPost("/api/v1/queue-entries/{idOrCode}/return-to-doctor")]
    [HttpPost("/api/v1/visits/{idOrCode}/return-to-doctor")]
    [HttpPost("/api/v1/reception/visits/{idOrCode}/return-to-doctor")]
    [ProducesResponseType<CreateReturnToDoctorQueueEntryResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateReturnToDoctorQueueEntryResponseDto>> CreateReturnToDoctorQueueEntry(
        string idOrCode,
        [FromBody] CreateReturnToDoctorQueueEntryRequest request,
        CancellationToken ct)
        => Ok(await _examinationQueueService.CreateReturnToDoctorQueueEntryAsync(GetUserId(), idOrCode, request, GetIp(), ct));

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
