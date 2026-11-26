using System.Security.Claims;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = $"{RoleNames.Patient},{RoleNames.Doctor},{RoleNames.Reception},{RoleNames.HospitalAdmin}")]
public sealed class VisitDiagnosticOrderSelectionController : ControllerBase
{
    private readonly IDiagnosticOrderSelectionService _service;

    public VisitDiagnosticOrderSelectionController(IDiagnosticOrderSelectionService service)
    {
        _service = service;
    }

    /// <summary>POST /api/v1/visits/{idOrCode}/diagnostic-orders/selection — Chọn / bỏ chọn chỉ định tùy chọn hoặc yêu cầu bác sĩ xem xét lại chỉ định bắt buộc (ADR-0060).</summary>
    [HttpPost("/api/v1/visits/{idOrCode}/diagnostic-orders/selection")]
    [HttpPost("/api/v1/patients/visits/{idOrCode}/diagnostic-orders/selection")]
    [HttpPost("/api/v1/reception/visits/{idOrCode}/diagnostic-orders/selection")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/diagnostic-orders/selection")]
    [ProducesResponseType<VisitChargesResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitChargesResponseDto>> UpdateSelection(
        [FromRoute] string idOrCode,
        [FromBody] UpdateDiagnosticOrderSelectionRequestDto? request,
        CancellationToken ct)
        => Ok(await _service.UpdateSelectionAsync(GetUserId(), idOrCode, request, GetIp(), ct));

    /// <summary>POST /api/v1/visits/{idOrCode}/diagnostic-orders/{orderIdOrCode}/decline — Từ chối chỉ định CLS tùy chọn chưa trả hoặc ghi nhận bỏ qua sau thanh toán (SKIPPED_AFTER_PAID / PAID_NOT_PERFORMED).</summary>
    [HttpPost("/api/v1/visits/{idOrCode}/diagnostic-orders/{orderIdOrCode}/decline")]
    [HttpPost("/api/v1/patients/visits/{idOrCode}/diagnostic-orders/{orderIdOrCode}/decline")]
    [HttpPost("/api/v1/reception/visits/{idOrCode}/diagnostic-orders/{orderIdOrCode}/decline")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/diagnostic-orders/{orderIdOrCode}/decline")]
    [ProducesResponseType<DeclineDiagnosticOrderResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeclineDiagnosticOrderResponseDto>> Decline(
        [FromRoute] string idOrCode,
        [FromRoute] string orderIdOrCode,
        [FromBody] DeclineDiagnosticOrderRequestDto? request,
        CancellationToken ct)
        => Ok(await _service.DeclineOrderAsync(GetUserId(), idOrCode, orderIdOrCode, request, GetIp(), ct));

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Token không hợp lệ");

        return userId;
    }

    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();
}
