using System.Security.Claims;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "DIAGNOSTIC_STAFF")]
public sealed class DiagnosticResultsController : ControllerBase
{
    private readonly IDiagnosticResultEntryService _resultEntryService;

    public DiagnosticResultsController(IDiagnosticResultEntryService resultEntryService)
    {
        _resultEntryService = resultEntryService;
    }

    /// <summary>
    /// GET /api/v1/diagnostic/results — Xem danh sách phiếu chỉ định cận lâm sàng chờ nhập kết quả &amp; kết luận (UC43 / #134).
    /// </summary>
    [HttpGet("/api/v1/diagnostic/results")]
    [HttpGet("/api/v1/diagnostic/orders/results")]
    [HttpGet("/api/v1/diagnostic/orders/result")]
    [HttpGet("/api/v1/diagnostic/orders/enter-result")]
    [HttpGet("/api/v1/diagnostic-results")]
    [HttpGet("/api/v1/diagnostic-orders/results")]
    [HttpGet("/api/v1/diagnostic-orders/enter-result")]
    [HttpGet("/api/v1/diagnostic/workbench/results")]
    [ProducesResponseType<DiagnosticResultEntryListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticResultEntryListResponseDto>> GetOrdersForResultEntry(
        [FromQuery] GetDiagnosticOrdersForResultEntryRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _resultEntryService.GetOrdersForResultEntryAsync(GetUserId(), request, ct));

    /// <summary>
    /// GET /api/v1/diagnostic/results/{idOrCode} — Xem chi tiết phiếu chỉ định, thông tin lâm sàng, lịch sử phiên bản &amp; kết quả cận lâm sàng (UC43 / #134).
    /// </summary>
    [HttpGet("/api/v1/diagnostic/results/{idOrCode}")]
    [HttpGet("/api/v1/diagnostic/orders/{idOrCode}/result")]
    [HttpGet("/api/v1/diagnostic/orders/{idOrCode}/results")]
    [HttpGet("/api/v1/diagnostic/orders/{idOrCode}/enter-result")]
    [HttpGet("/api/v1/diagnostic-results/{idOrCode}")]
    [HttpGet("/api/v1/diagnostic-orders/{idOrCode}/result")]
    [HttpGet("/api/v1/diagnostic-orders/{idOrCode}/results")]
    [HttpGet("/api/v1/diagnostic-orders/{idOrCode}/enter-result")]
    [HttpGet("/api/v1/diagnostic/workbench/orders/{idOrCode}/result")]
    [ProducesResponseType<DiagnosticResultEntryDetailResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticResultEntryDetailResponseDto>> GetOrderResultByIdOrCode(
        string idOrCode,
        CancellationToken ct = default)
        => Ok(await _resultEntryService.GetOrderResultByIdOrCodeAsync(GetUserId(), idOrCode, ct));

    /// <summary>
    /// POST/PUT/PATCH /api/v1/diagnostic/orders/{idOrCode}/result — Nhập kết quả và kết luận cận lâm sàng cho dịch vụ đã thực hiện (UC43 / #134).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/result")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/result")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/result")]
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/results")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/results")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/results")]
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/enter-result")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/enter-result")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/enter-result")]
    [HttpPost("/api/v1/diagnostic/results/{idOrCode}")]
    [HttpPut("/api/v1/diagnostic/results/{idOrCode}")]
    [HttpPatch("/api/v1/diagnostic/results/{idOrCode}")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/result")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/result")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/result")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/results")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/results")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/results")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/enter-result")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/enter-result")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/enter-result")]
    [HttpPost("/api/v1/diagnostic-results/{idOrCode}")]
    [HttpPut("/api/v1/diagnostic-results/{idOrCode}")]
    [HttpPatch("/api/v1/diagnostic-results/{idOrCode}")]
    [HttpPost("/api/v1/diagnostic/workbench/orders/{idOrCode}/result")]
    [HttpPut("/api/v1/diagnostic/workbench/orders/{idOrCode}/result")]
    [HttpPatch("/api/v1/diagnostic/workbench/orders/{idOrCode}/result")]
    [ProducesResponseType<EnterDiagnosticResultResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EnterDiagnosticResultResponseDto>> EnterResult(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EnterDiagnosticResultRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _resultEntryService.EnterDiagnosticResultAsync(
            GetUserId(),
            idOrCode,
            request,
            GetIp(),
            ct));

    /// <summary>
    /// POST/PUT /api/v1/diagnostic/results — Nhập kết quả cận lâm sàng theo OrderId / OrderCode trong payload (UC43 / #134).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/results")]
    [HttpPut("/api/v1/diagnostic/results")]
    [HttpPost("/api/v1/diagnostic/orders/result")]
    [HttpPut("/api/v1/diagnostic/orders/result")]
    [HttpPost("/api/v1/diagnostic/orders/results")]
    [HttpPut("/api/v1/diagnostic/orders/results")]
    [HttpPost("/api/v1/diagnostic/orders/enter-result")]
    [HttpPut("/api/v1/diagnostic/orders/enter-result")]
    [HttpPost("/api/v1/diagnostic-results")]
    [HttpPut("/api/v1/diagnostic-results")]
    [HttpPost("/api/v1/diagnostic-orders/result")]
    [HttpPut("/api/v1/diagnostic-orders/result")]
    [HttpPost("/api/v1/diagnostic-orders/results")]
    [HttpPut("/api/v1/diagnostic-orders/results")]
    [HttpPost("/api/v1/diagnostic-orders/enter-result")]
    [HttpPut("/api/v1/diagnostic-orders/enter-result")]
    [ProducesResponseType<EnterDiagnosticResultResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EnterDiagnosticResultResponseDto>> EnterResultFromBody(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EnterDiagnosticResultRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _resultEntryService.EnterDiagnosticResultAsync(
            GetUserId(),
            null,
            request,
            GetIp(),
            ct));

    /// <summary>
    /// POST/PUT/PATCH .../result/amend — Sửa kết quả cận lâm sàng đã khóa (UC43 / #136).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/result/amend")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/result/amend")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/result/amend")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/result/amend")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/result/amend")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/result/amend")]
    [HttpPost("/api/v1/diagnostic/results/{idOrCode}/amend")]
    [HttpPut("/api/v1/diagnostic/results/{idOrCode}/amend")]
    [HttpPatch("/api/v1/diagnostic/results/{idOrCode}/amend")]
    [HttpPost("/api/v1/diagnostic-results/{idOrCode}/amend")]
    [HttpPut("/api/v1/diagnostic-results/{idOrCode}/amend")]
    [HttpPatch("/api/v1/diagnostic-results/{idOrCode}/amend")]
    [ProducesResponseType<EnterDiagnosticResultResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EnterDiagnosticResultResponseDto>> AmendResult(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateDiagnosticResultRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _resultEntryService.UpdateDiagnosticResultAsync(
            GetUserId(),
            idOrCode,
            request,
            GetIp(),
            ct));

    /// <summary>
    /// POST/PUT .../results/amend — Sửa kết quả đã khóa theo OrderId / OrderCode trong payload (UC43 / #136).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/results/amend")]
    [HttpPut("/api/v1/diagnostic/results/amend")]
    [HttpPost("/api/v1/diagnostic/orders/result/amend")]
    [HttpPut("/api/v1/diagnostic/orders/result/amend")]
    [ProducesResponseType<EnterDiagnosticResultResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EnterDiagnosticResultResponseDto>> AmendResultFromBody(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateDiagnosticResultRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _resultEntryService.UpdateDiagnosticResultAsync(
            GetUserId(),
            null,
            request,
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

    private string? GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
