using System.Security.Claims;
using System.Text.Json;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = $"{RoleNames.Patient},{RoleNames.Doctor},{RoleNames.Reception},{RoleNames.HospitalAdmin}")]
public sealed class VisitQrPaymentsController : ControllerBase
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IGenerateQrPaymentService _service;

    public VisitQrPaymentsController(IGenerateQrPaymentService service)
    {
        _service = service;
    }

    /// <summary>POST /api/v1/visits/{idOrCode}/payments/qr — Phát hành VietQR đơn lẻ hoặc gộp theo các khoản phí được chọn (UC18 / ADR-0060).</summary>
    [HttpPost("/api/v1/visits/{idOrCode}/payments/qr")]
    [HttpPost("/api/v1/patients/visits/{idOrCode}/payments/qr")]
    [HttpPost("/api/v1/doctor/visits/{idOrCode}/payments/qr")]
    [HttpPost("/api/v1/reception/visits/{idOrCode}/payments/qr")]
    [EnableRateLimiting("payment-qr-generate")]
    [ProducesResponseType<GenerateQrPaymentResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<GenerateQrPaymentResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<GenerateQrPaymentResponseDto>> Create(
        [FromRoute] string idOrCode,
        CancellationToken ct)
    {
        var request = await TryReadOptionalBodyAsync(ct);

        var result = request is null
            ? await _service.CreateAsync(GetUserId(), idOrCode, GetIp(), ct)
            : await _service.CreateAsync(GetUserId(), idOrCode, GetIp(), request, ct);

        if (result.IssueKind == QrPaymentIssueKind.Created)
            return StatusCode(StatusCodes.Status201Created, result);
        return Ok(result);
    }

    private async Task<GenerateQrPaymentRequestDto?> TryReadOptionalBodyAsync(CancellationToken ct)
    {
        var httpRequest = HttpContext.Request;
        if (httpRequest.ContentLength is 0)
            return null;

        if (httpRequest.ContentLength is null &&
            !httpRequest.Headers.ContainsKey("Transfer-Encoding") &&
            string.IsNullOrWhiteSpace(httpRequest.ContentType))
        {
            return null;
        }

        using var reader = new StreamReader(httpRequest.Body, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GenerateQrPaymentRequestDto>(raw, RequestJsonOptions);
        }
        catch (JsonException)
        {
            throw new ValidationException("Dữ liệu yêu cầu tạo mã QR không hợp lệ.");
        }
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
