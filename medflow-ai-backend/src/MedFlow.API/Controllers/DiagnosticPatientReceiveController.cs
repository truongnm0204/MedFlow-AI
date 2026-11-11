using System.Security.Claims;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "DIAGNOSTIC_STAFF")]
public sealed class DiagnosticPatientReceiveController : ControllerBase
{
    private readonly IDiagnosticPatientReceiveService _patientReceiveService;

    public DiagnosticPatientReceiveController(IDiagnosticPatientReceiveService patientReceiveService)
    {
        _patientReceiveService = patientReceiveService;
    }

    /// <summary>
    /// GET /api/v1/diagnostic/orders/receive — Xem danh sách phiếu chỉ định cận lâm sàng chờ tiếp nhận bệnh nhân tại điểm dịch vụ (UC40 / #128).
    /// </summary>
    [HttpGet("/api/v1/diagnostic/orders/receive")]
    [HttpGet("/api/v1/diagnostic/orders/receive-patient")]
    [HttpGet("/api/v1/diagnostic-orders/receive")]
    [HttpGet("/api/v1/diagnostic/patients/receive")]
    [HttpGet("/api/v1/diagnostic/receive-patient")]
    [HttpGet("/api/v1/diagnostic/workbench/receive")]
    [ProducesResponseType<DiagnosticPatientReceiveListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticPatientReceiveListResponseDto>> GetOrdersForReceive(
        [FromQuery] GetDiagnosticOrdersForReceiveRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _patientReceiveService.GetOrdersForReceiveAsync(GetUserId(), request, ct));

    /// <summary>
    /// GET /api/v1/diagnostic/orders/{idOrCode}/receive — Xem chi tiết phiếu chỉ định cận lâm sàng &amp; điều kiện tiếp nhận bệnh nhân tại điểm dịch vụ (UC40 / #128).
    /// </summary>
    [HttpGet("/api/v1/diagnostic/orders/{idOrCode}/receive")]
    [HttpGet("/api/v1/diagnostic/orders/{idOrCode}/receive-patient")]
    [HttpGet("/api/v1/diagnostic-orders/{idOrCode}/receive")]
    [HttpGet("/api/v1/diagnostic/patients/{idOrCode}/receive")]
    [HttpGet("/api/v1/diagnostic/workbench/orders/{idOrCode}/receive")]
    [ProducesResponseType<DiagnosticPatientReceiveDetailResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticPatientReceiveDetailResponseDto>> GetOrderForReceiveByIdOrCode(
        string idOrCode,
        CancellationToken ct = default)
        => Ok(await _patientReceiveService.GetOrderForReceiveByIdOrCodeAsync(GetUserId(), idOrCode, ct));

    /// <summary>
    /// POST/PUT/PATCH /api/v1/diagnostic/orders/{idOrCode}/receive — Tiếp nhận bệnh nhân tại điểm dịch vụ cận lâm sàng, chuyển trạng thái hàng đợi sang READY (UC40 / #128).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/receive")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/receive")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/receive")]
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/receive-patient")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/receive-patient")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/receive-patient")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/receive")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/receive")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/receive")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/receive-patient")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/receive-patient")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/receive-patient")]
    [HttpPost("/api/v1/diagnostic/patients/{idOrCode}/receive")]
    [HttpPut("/api/v1/diagnostic/patients/{idOrCode}/receive")]
    [HttpPatch("/api/v1/diagnostic/patients/{idOrCode}/receive")]
    [HttpPost("/api/v1/diagnostic/workbench/orders/{idOrCode}/receive")]
    [HttpPut("/api/v1/diagnostic/workbench/orders/{idOrCode}/receive")]
    [HttpPatch("/api/v1/diagnostic/workbench/orders/{idOrCode}/receive")]
    [ProducesResponseType<ReceiveDiagnosticPatientResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReceiveDiagnosticPatientResponseDto>> ReceivePatient(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReceiveDiagnosticPatientRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _patientReceiveService.ReceiveDiagnosticPatientAsync(
            GetUserId(),
            idOrCode,
            request,
            GetIp(),
            ct));

    /// <summary>
    /// POST/PUT /api/v1/diagnostic/orders/receive — Tiếp nhận bệnh nhân hàng loạt cho các phiếu chỉ định cận lâm sàng đã xác nhận (UC40 / #128).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/orders/receive")]
    [HttpPut("/api/v1/diagnostic/orders/receive")]
    [HttpPost("/api/v1/diagnostic/orders/receive-patient")]
    [HttpPut("/api/v1/diagnostic/orders/receive-patient")]
    [HttpPost("/api/v1/diagnostic-orders/receive")]
    [HttpPut("/api/v1/diagnostic-orders/receive")]
    [HttpPost("/api/v1/diagnostic/patients/receive")]
    [HttpPut("/api/v1/diagnostic/patients/receive")]
    [HttpPost("/api/v1/diagnostic/receive-patient")]
    [HttpPut("/api/v1/diagnostic/receive-patient")]
    [ProducesResponseType<ReceiveDiagnosticPatientResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReceiveDiagnosticPatientResponseDto>> ReceivePatientsBatch(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReceiveDiagnosticPatientsBatchRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _patientReceiveService.ReceiveDiagnosticPatientsBatchAsync(
            GetUserId(),
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

    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();
}
