using System.Security.Claims;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "DIAGNOSTIC_STAFF")]
public sealed class DiagnosticServiceExecutionController : ControllerBase
{
    private readonly IDiagnosticServiceExecutionService _executionService;

    public DiagnosticServiceExecutionController(IDiagnosticServiceExecutionService executionService)
    {
        _executionService = executionService;
    }

    /// <summary>
    /// GET /api/v1/diagnostic/orders/execution — Xem danh sách phiếu chỉ định cận lâm sàng theo trạng thái thực hiện (UC42 / #132).
    /// </summary>
    [HttpGet("/api/v1/diagnostic/orders/execution")]
    [HttpGet("/api/v1/diagnostic/orders/execution-status")]
    [HttpGet("/api/v1/diagnostic/orders/service-status")]
    [HttpGet("/api/v1/diagnostic/execution")]
    [HttpGet("/api/v1/diagnostic/service-status")]
    [HttpGet("/api/v1/diagnostic-orders/execution")]
    [HttpGet("/api/v1/diagnostic-orders/execution-status")]
    [HttpGet("/api/v1/diagnostic-orders/service-status")]
    [HttpGet("/api/v1/diagnostic/workbench/execution")]
    [ProducesResponseType<DiagnosticServiceExecutionListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticServiceExecutionListResponseDto>> GetExecutions(
        [FromQuery] GetDiagnosticServiceExecutionsRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _executionService.GetExecutionsAsync(GetUserId(), request, ct));

    /// <summary>
    /// GET /api/v1/diagnostic/orders/{idOrCode}/execution — Xem chi tiết thực hiện kỹ thuật &amp; lịch sử chuyển trạng thái (UC42 / #132).
    /// </summary>
    [HttpGet("/api/v1/diagnostic/orders/{idOrCode}/execution")]
    [HttpGet("/api/v1/diagnostic/orders/{idOrCode}/execution-status")]
    [HttpGet("/api/v1/diagnostic/orders/{idOrCode}/service-status")]
    [HttpGet("/api/v1/diagnostic/execution/{idOrCode:regex(^(?!status$|start$|complete$).+)}")]
    [HttpGet("/api/v1/diagnostic-orders/{idOrCode}/execution")]
    [HttpGet("/api/v1/diagnostic-orders/{idOrCode}/execution-status")]
    [HttpGet("/api/v1/diagnostic-orders/{idOrCode}/service-status")]
    [HttpGet("/api/v1/diagnostic/workbench/orders/{idOrCode}/execution")]
    [ProducesResponseType<DiagnosticServiceExecutionDetailResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticServiceExecutionDetailResponseDto>> GetExecutionByIdOrCode(
        string idOrCode,
        CancellationToken ct = default)
        => Ok(await _executionService.GetExecutionByIdOrCodeAsync(GetUserId(), idOrCode, ct));

    /// <summary>
    /// PUT/PATCH/POST /api/v1/diagnostic/orders/{idOrCode}/execution-status — Cập nhật trạng thái thực hiện kỹ thuật (UC42 / #132).
    /// </summary>
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/execution-status")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/execution-status")]
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/execution-status")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/service-status")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/service-status")]
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/service-status")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/execution")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/execution")]
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/execution")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/status")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/status")]
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/status")]
    [HttpPut("/api/v1/diagnostic/execution/{idOrCode}/status")]
    [HttpPatch("/api/v1/diagnostic/execution/{idOrCode}/status")]
    [HttpPost("/api/v1/diagnostic/execution/{idOrCode}/status")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/execution-status")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/execution-status")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/execution-status")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/service-status")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/service-status")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/service-status")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/status")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/status")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/status")]
    [HttpPut("/api/v1/diagnostic/workbench/orders/{idOrCode}/execution-status")]
    [HttpPatch("/api/v1/diagnostic/workbench/orders/{idOrCode}/execution-status")]
    [HttpPost("/api/v1/diagnostic/workbench/orders/{idOrCode}/execution-status")]
    [ProducesResponseType<UpdateDiagnosticServiceStatusResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdateDiagnosticServiceStatusResponseDto>> UpdateExecutionStatus(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateDiagnosticServiceStatusRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _executionService.UpdateServiceExecutionStatusAsync(GetUserId(), idOrCode, request, GetIp(), ct));

    /// <summary>
    /// POST/PUT/PATCH /api/v1/diagnostic/orders/{idOrCode}/start — Bắt đầu thực hiện kỹ thuật (CONFIRMED -&gt; IN_PROGRESS) (UC42 / #132).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/start")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/start")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/start")]
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/start-execution")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/start-execution")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/start-execution")]
    [HttpPost("/api/v1/diagnostic/execution/{idOrCode}/start")]
    [HttpPut("/api/v1/diagnostic/execution/{idOrCode}/start")]
    [HttpPatch("/api/v1/diagnostic/execution/{idOrCode}/start")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/start")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/start")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/start")]
    [ProducesResponseType<UpdateDiagnosticServiceStatusResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdateDiagnosticServiceStatusResponseDto>> StartExecution(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateDiagnosticServiceStatusRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _executionService.StartServiceExecutionAsync(GetUserId(), idOrCode, request, GetIp(), ct));

    /// <summary>
    /// POST/PUT/PATCH /api/v1/diagnostic/orders/{idOrCode}/complete — Hoàn tất thực hiện kỹ thuật (IN_PROGRESS -&gt; COMPLETED) (UC42 / #132).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/complete")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/complete")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/complete")]
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/complete-execution")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/complete-execution")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/complete-execution")]
    [HttpPost("/api/v1/diagnostic/execution/{idOrCode}/complete")]
    [HttpPut("/api/v1/diagnostic/execution/{idOrCode}/complete")]
    [HttpPatch("/api/v1/diagnostic/execution/{idOrCode}/complete")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/complete")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/complete")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/complete")]
    [ProducesResponseType<UpdateDiagnosticServiceStatusResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdateDiagnosticServiceStatusResponseDto>> CompleteExecution(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateDiagnosticServiceStatusRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _executionService.CompleteServiceExecutionAsync(GetUserId(), idOrCode, request, GetIp(), ct));

    /// <summary>
    /// POST/PUT/PATCH /api/v1/diagnostic/orders/execution-status — Cập nhật trạng thái thực hiện hàng loạt (batch) (UC42 / #132).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/orders/execution-status")]
    [HttpPut("/api/v1/diagnostic/orders/execution-status")]
    [HttpPatch("/api/v1/diagnostic/orders/execution-status")]
    [HttpPost("/api/v1/diagnostic/orders/service-status")]
    [HttpPut("/api/v1/diagnostic/orders/service-status")]
    [HttpPatch("/api/v1/diagnostic/orders/service-status")]
    [HttpPost("/api/v1/diagnostic/execution/status")]
    [HttpPut("/api/v1/diagnostic/execution/status")]
    [HttpPatch("/api/v1/diagnostic/execution/status")]
    [HttpPost("/api/v1/diagnostic-orders/execution-status")]
    [HttpPut("/api/v1/diagnostic-orders/execution-status")]
    [HttpPatch("/api/v1/diagnostic-orders/execution-status")]
    [HttpPost("/api/v1/diagnostic-orders/service-status")]
    [HttpPut("/api/v1/diagnostic-orders/service-status")]
    [HttpPatch("/api/v1/diagnostic-orders/service-status")]
    [ProducesResponseType<UpdateDiagnosticServiceStatusResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdateDiagnosticServiceStatusResponseDto>> UpdateExecutionStatusBatch(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateDiagnosticServiceStatusBatchRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _executionService.UpdateServiceExecutionStatusBatchAsync(GetUserId(), request, GetIp(), ct));

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

    private string? GetIp()
        => Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded) && !string.IsNullOrWhiteSpace(forwarded)
            ? forwarded.ToString().Split(',')[0].Trim()
            : HttpContext.Connection.RemoteIpAddress?.ToString();
}
