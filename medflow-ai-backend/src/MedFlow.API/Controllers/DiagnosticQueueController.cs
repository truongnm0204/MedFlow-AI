using System.Security.Claims;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "DIAGNOSTIC_STAFF")]
public sealed class DiagnosticQueueController : ControllerBase
{
    private readonly IDiagnosticQueueService _diagnosticQueueService;

    public DiagnosticQueueController(IDiagnosticQueueService diagnosticQueueService)
    {
        _diagnosticQueueService = diagnosticQueueService;
    }

    /// <summary>
    /// GET /api/v1/diagnostic/queue — Xem bảng hàng đợi cận lâm sàng theo điểm phục vụ &amp; phòng kỹ thuật (UC41 / #130).
    /// </summary>
    [HttpGet("/api/v1/diagnostic/queue")]
    [HttpGet("/api/v1/diagnostic-queue")]
    [HttpGet("/api/v1/diagnostic/queues")]
    [HttpGet("/api/v1/diagnostic/workbench/queue")]
    [HttpGet("/api/v1/diagnostic/queue-entries")]
    [ProducesResponseType<DiagnosticQueueBoardResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticQueueBoardResponseDto>> GetDiagnosticQueue(
        [FromQuery] GetDiagnosticQueueRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.GetDiagnosticQueueAsync(GetUserId(), request, ct));

    /// <summary>
    /// GET /api/v1/diagnostic/queue/{idOrCode} — Xem chi tiết lượt xếp hàng cận lâm sàng &amp; lịch sử chuyển trạng thái (UC41 / #130).
    /// </summary>
    [HttpGet("/api/v1/diagnostic/queue/{idOrCode:regex(^(?!call-next$|service-points$).+)}")]
    [HttpGet("/api/v1/diagnostic/queue/entries/{idOrCode}")]
    [HttpGet("/api/v1/diagnostic/queue-entries/{idOrCode:regex(^(?!call-next$|service-points$).+)}")]
    [HttpGet("/api/v1/diagnostic-queue/{idOrCode:regex(^(?!call-next$|service-points$).+)}")]
    [HttpGet("/api/v1/diagnostic-queue/entries/{idOrCode}")]
    [HttpGet("/api/v1/diagnostic/queue/{idOrCode}/status")]
    [HttpGet("/api/v1/diagnostic/queue-entries/{idOrCode}/status")]
    [ProducesResponseType<DiagnosticQueueEntryDetailResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiagnosticQueueEntryDetailResponseDto>> GetDiagnosticQueueEntryDetail(
        string idOrCode,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.GetDiagnosticQueueEntryByIdOrCodeAsync(GetUserId(), idOrCode, ct));

    /// <summary>
    /// POST /api/v1/diagnostic/queue/service-points/{servicePointId}/call-next — Gọi bệnh nhân đủ điều kiện tiếp theo tại điểm phục vụ cận lâm sàng (UC41 / #130).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/queue/service-points/{servicePointId}/call-next")]
    [HttpPost("/api/v1/diagnostic/service-points/{servicePointId}/call-next")]
    [HttpPost("/api/v1/diagnostic-queue/service-points/{servicePointId}/call-next")]
    [HttpPost("/api/v1/diagnostic/workbench/service-points/{servicePointId}/call-next")]
    [ProducesResponseType<CallNextDiagnosticPatientResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CallNextDiagnosticPatientResponseDto>> CallNextByServicePoint(
        string servicePointId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CallNextDiagnosticPatientRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.CallNextDiagnosticPatientAsync(
            GetUserId(),
            servicePointId,
            request,
            GetIp(),
            ct));

    /// <summary>
    /// POST /api/v1/diagnostic/queue/call-next — Gọi bệnh nhân tiếp theo theo ServicePointId trong payload (UC41 / #130).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/queue/call-next")]
    [HttpPost("/api/v1/diagnostic-queue/call-next")]
    [ProducesResponseType<CallNextDiagnosticPatientResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CallNextDiagnosticPatientResponseDto>> CallNext(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CallNextDiagnosticPatientRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.CallNextDiagnosticPatientAsync(
            GetUserId(),
            request?.ServicePointId ?? string.Empty,
            request,
            GetIp(),
            ct));

    /// <summary>
    /// PUT/PATCH/POST /api/v1/diagnostic/queue/{idOrCode}/status — Cập nhật trạng thái lượt xếp hàng cận lâm sàng (UC41 / #130).
    /// </summary>
    [HttpPut("/api/v1/diagnostic/queue/{idOrCode}/status")]
    [HttpPatch("/api/v1/diagnostic/queue/{idOrCode}/status")]
    [HttpPost("/api/v1/diagnostic/queue/{idOrCode}/status")]
    [HttpPut("/api/v1/diagnostic/queue/entries/{idOrCode}/status")]
    [HttpPatch("/api/v1/diagnostic/queue/entries/{idOrCode}/status")]
    [HttpPost("/api/v1/diagnostic/queue/entries/{idOrCode}/status")]
    [HttpPut("/api/v1/diagnostic/queue-entries/{idOrCode}/status")]
    [HttpPatch("/api/v1/diagnostic/queue-entries/{idOrCode}/status")]
    [HttpPost("/api/v1/diagnostic/queue-entries/{idOrCode}/status")]
    [HttpPut("/api/v1/diagnostic-queue/{idOrCode}/status")]
    [HttpPatch("/api/v1/diagnostic-queue/{idOrCode}/status")]
    [HttpPost("/api/v1/diagnostic-queue/{idOrCode}/status")]
    [HttpPut("/api/v1/diagnostic-queue/entries/{idOrCode}/status")]
    [HttpPatch("/api/v1/diagnostic-queue/entries/{idOrCode}/status")]
    [HttpPost("/api/v1/diagnostic-queue/entries/{idOrCode}/status")]
    [ProducesResponseType<ManageDiagnosticQueueActionResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManageDiagnosticQueueActionResponseDto>> UpdateStatus(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateDiagnosticQueueStatusRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.UpdateDiagnosticQueueEntryStatusAsync(
            GetUserId(),
            idOrCode,
            request ?? new UpdateDiagnosticQueueStatusRequest(),
            GetIp(),
            ct));

    /// <summary>
    /// POST /api/v1/diagnostic/queue/{idOrCode}/ready — Đánh dấu bệnh nhân sẵn sàng trong hàng đợi cận lâm sàng (WAITING/RESERVED -&gt; READY) (UC41 / #130).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/queue/{idOrCode}/ready")]
    [HttpPost("/api/v1/diagnostic/queue/entries/{idOrCode}/ready")]
    [HttpPost("/api/v1/diagnostic/queue-entries/{idOrCode}/ready")]
    [HttpPost("/api/v1/diagnostic-queue/{idOrCode}/ready")]
    [ProducesResponseType<ManageDiagnosticQueueActionResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManageDiagnosticQueueActionResponseDto>> MarkReady(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DiagnosticQueueTurnActionRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.UpdateDiagnosticQueueEntryStatusAsync(
            GetUserId(),
            idOrCode,
            new UpdateDiagnosticQueueStatusRequest(
                TargetStatus: "READY",
                PresenceStatus: request?.PresenceStatus ?? "PRESENT",
                Reason: request?.Reason,
                Notes: request?.Notes,
                ExpectedStateVersion: request?.ExpectedStateVersion),
            GetIp(),
            ct));

    /// <summary>
    /// POST /api/v1/diagnostic/queue/{idOrCode}/call — Gọi trực tiếp một lượt xếp hàng cận lâm sàng cụ thể (WAITING/READY -&gt; CALLED) (UC41 / #130).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/queue/{idOrCode}/call")]
    [HttpPost("/api/v1/diagnostic/queue/entries/{idOrCode}/call")]
    [HttpPost("/api/v1/diagnostic/queue-entries/{idOrCode}/call")]
    [HttpPost("/api/v1/diagnostic-queue/{idOrCode}/call")]
    [ProducesResponseType<ManageDiagnosticQueueActionResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManageDiagnosticQueueActionResponseDto>> CallEntry(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DiagnosticQueueTurnActionRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.UpdateDiagnosticQueueEntryStatusAsync(
            GetUserId(),
            idOrCode,
            new UpdateDiagnosticQueueStatusRequest(
                TargetStatus: "CALLED",
                PresenceStatus: request?.PresenceStatus ?? "PRESENT",
                Reason: request?.Reason,
                Notes: request?.Notes,
                ExpectedStateVersion: request?.ExpectedStateVersion),
            GetIp(),
            ct));

    /// <summary>
    /// POST /api/v1/diagnostic/queue/{idOrCode}/start — Bắt đầu thực hiện kỹ thuật cận lâm sàng (CALLED/READY -&gt; IN_PROGRESS) (UC41 / #130).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/queue/{idOrCode}/start")]
    [HttpPost("/api/v1/diagnostic/queue/entries/{idOrCode}/start")]
    [HttpPost("/api/v1/diagnostic/queue-entries/{idOrCode}/start")]
    [HttpPost("/api/v1/diagnostic-queue/{idOrCode}/start")]
    [ProducesResponseType<ManageDiagnosticQueueActionResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManageDiagnosticQueueActionResponseDto>> StartExecution(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DiagnosticQueueTurnActionRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.UpdateDiagnosticQueueEntryStatusAsync(
            GetUserId(),
            idOrCode,
            new UpdateDiagnosticQueueStatusRequest(
                TargetStatus: "IN_PROGRESS",
                PresenceStatus: "PRESENT",
                Reason: request?.Reason,
                Notes: request?.Notes,
                ExpectedStateVersion: request?.ExpectedStateVersion),
            GetIp(),
            ct));

    /// <summary>
    /// POST /api/v1/diagnostic/queue/{idOrCode}/complete — Hoàn tất thực hiện lượt xếp hàng cận lâm sàng (IN_PROGRESS -&gt; COMPLETED) (UC41 / #130).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/queue/{idOrCode}/complete")]
    [HttpPost("/api/v1/diagnostic/queue/entries/{idOrCode}/complete")]
    [HttpPost("/api/v1/diagnostic/queue-entries/{idOrCode}/complete")]
    [HttpPost("/api/v1/diagnostic-queue/{idOrCode}/complete")]
    [ProducesResponseType<ManageDiagnosticQueueActionResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManageDiagnosticQueueActionResponseDto>> CompleteExecution(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DiagnosticQueueTurnActionRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.UpdateDiagnosticQueueEntryStatusAsync(
            GetUserId(),
            idOrCode,
            new UpdateDiagnosticQueueStatusRequest(
                TargetStatus: "COMPLETED",
                PresenceStatus: request?.PresenceStatus ?? "PRESENT",
                Reason: request?.Reason,
                Notes: request?.Notes,
                ExpectedStateVersion: request?.ExpectedStateVersion),
            GetIp(),
            ct));

    /// <summary>
    /// POST /api/v1/diagnostic/queue/{idOrCode}/missed-turn — Đánh dấu bệnh nhân vắng mặt / qua lượt (CALLED/READY/WAITING -&gt; SKIPPED) (UC41 / #130).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/queue/{idOrCode}/missed-turn")]
    [HttpPost("/api/v1/diagnostic/queue/{idOrCode}/skip")]
    [HttpPost("/api/v1/diagnostic/queue/entries/{idOrCode}/missed-turn")]
    [HttpPost("/api/v1/diagnostic/queue/entries/{idOrCode}/skip")]
    [HttpPost("/api/v1/diagnostic/queue-entries/{idOrCode}/missed-turn")]
    [HttpPost("/api/v1/diagnostic/queue-entries/{idOrCode}/skip")]
    [HttpPost("/api/v1/diagnostic-queue/{idOrCode}/missed-turn")]
    [ProducesResponseType<ManageDiagnosticQueueActionResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManageDiagnosticQueueActionResponseDto>> MarkMissedTurn(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DiagnosticQueueTurnActionRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.MarkDiagnosticMissedTurnAsync(
            GetUserId(),
            idOrCode,
            request,
            GetIp(),
            ct));

    /// <summary>
    /// POST /api/v1/diagnostic/queue/{idOrCode}/restore-turn — Khôi phục lượt chờ cho bệnh nhân qua lượt đã quay lại (SKIPPED -&gt; READY) (UC41 / #130).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/queue/{idOrCode}/restore-turn")]
    [HttpPost("/api/v1/diagnostic/queue/{idOrCode}/restore")]
    [HttpPost("/api/v1/diagnostic/queue/entries/{idOrCode}/restore-turn")]
    [HttpPost("/api/v1/diagnostic/queue/entries/{idOrCode}/restore")]
    [HttpPost("/api/v1/diagnostic/queue-entries/{idOrCode}/restore-turn")]
    [HttpPost("/api/v1/diagnostic/queue-entries/{idOrCode}/restore")]
    [HttpPost("/api/v1/diagnostic-queue/{idOrCode}/restore-turn")]
    [ProducesResponseType<ManageDiagnosticQueueActionResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManageDiagnosticQueueActionResponseDto>> RestoreSkippedTurn(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DiagnosticQueueTurnActionRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.RestoreDiagnosticSkippedTurnAsync(
            GetUserId(),
            idOrCode,
            request,
            GetIp(),
            ct));

    /// <summary>
    /// POST /api/v1/diagnostic/queue/{idOrCode}/cancel — Hủy lượt xếp hàng cận lâm sàng (-&gt; CANCELLED) (UC41 / #130).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/queue/{idOrCode}/cancel")]
    [HttpPost("/api/v1/diagnostic/queue/entries/{idOrCode}/cancel")]
    [HttpPost("/api/v1/diagnostic/queue-entries/{idOrCode}/cancel")]
    [HttpPost("/api/v1/diagnostic-queue/{idOrCode}/cancel")]
    [HttpPost("/api/v1/diagnostic-queue/entries/{idOrCode}/cancel")]
    [ProducesResponseType<ManageDiagnosticQueueActionResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManageDiagnosticQueueActionResponseDto>> CancelQueueEntry(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelDiagnosticQueueEntryRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _diagnosticQueueService.CancelDiagnosticQueueEntryAsync(
            GetUserId(),
            idOrCode,
            request ?? new CancelDiagnosticQueueEntryRequest(),
            GetIp(),
            ct));

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
