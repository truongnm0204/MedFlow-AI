using System.Security.Claims;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = $"{RoleNames.Reception},{RoleNames.HospitalAdmin}")]
public sealed class VisitCashPaymentsController : ControllerBase
{
    private readonly ICollectCashPaymentService _service;

    public VisitCashPaymentsController(ICollectCashPaymentService service)
    {
        _service = service;
    }

    /// <summary>POST /api/v1/visits/{idOrCode}/payments/cash — Thu tiền mặt tại quầy tiếp nhận với kiểm tra quyền PAYMENT_COLLECT trực tiếp (ADR-0060).</summary>
    [HttpPost("/api/v1/visits/{idOrCode}/payments/cash")]
    [HttpPost("/api/v1/reception/visits/{idOrCode}/payments/cash")]
    [ProducesResponseType<CollectCashPaymentResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<CollectCashPaymentResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CollectCashPaymentResponseDto>> CollectCash(
        [FromRoute] string idOrCode,
        [FromBody] CollectCashPaymentRequestDto? request,
        CancellationToken ct)
    {
        var result = await _service.CollectAsync(GetUserId(), idOrCode, request, GetIp(), ct);
        if (result.IsIdempotentReplay)
            return Ok(result);
        return StatusCode(StatusCodes.Status201Created, result);
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
