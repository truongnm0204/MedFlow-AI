using System.Security.Claims;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "PATIENT")]
public sealed class VisitQrPaymentsController : ControllerBase
{
    private readonly IGenerateQrPaymentService _service;

    public VisitQrPaymentsController(IGenerateQrPaymentService service)
    {
        _service = service;
    }

    /// <summary>POST /api/v1/visits/{idOrCode}/payments/qr — Phát hành QR thanh toán cho bệnh nhân (UC18 / #162).</summary>
    [HttpPost("/api/v1/visits/{idOrCode}/payments/qr")]
    [HttpPost("/api/v1/patients/visits/{idOrCode}/payments/qr")]
    [ProducesResponseType<GenerateQrPaymentResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<GenerateQrPaymentResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GenerateQrPaymentResponseDto>> Create(
        [FromRoute] string idOrCode,
        CancellationToken ct)
    {
        var result = await _service.CreateAsync(GetUserId(), idOrCode, GetIp(), ct);
        if (result.IssueKind == QrPaymentIssueKind.Created)
            return StatusCode(StatusCodes.Status201Created, result);
        return Ok(result);
    }

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
