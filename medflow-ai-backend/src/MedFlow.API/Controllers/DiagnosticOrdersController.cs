using System.Security.Claims;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "DIAGNOSTIC_STAFF")]
public sealed class DiagnosticOrdersController : ControllerBase
{
    private readonly IDiagnosticOrderReviewService _orderReviewService;

    public DiagnosticOrdersController(IDiagnosticOrderReviewService orderReviewService)
    {
        _orderReviewService = orderReviewService;
    }

    /// <summary>
    /// GET /api/v1/diagnostic/orders/review — Xem danh sách phiếu chỉ định cận lâm sàng cần kiểm tra &amp; xác nhận (UC39 / #126).
    /// </summary>
    [HttpGet("/api/v1/diagnostic/orders/review")]
    [HttpGet("/api/v1/diagnostic-orders")]
    [HttpGet("/api/v1/diagnostic-orders/review")]
    [HttpGet("/api/v1/diagnostic/workbench/orders")]
    [ProducesResponseType<DiagnosticOrderReviewListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticOrderReviewListResponseDto>> GetOrders(
        [FromQuery] GetDiagnosticOrdersForReviewRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _orderReviewService.GetDiagnosticOrdersAsync(GetUserId(), request, ct));

    /// <summary>
    /// GET /api/v1/diagnostic/orders/{idOrCode}/review — Xem chi tiết phiếu chỉ định cận lâm sàng, thông tin lâm sàng &amp; điều kiện thực hiện (UC39 / #126).
    /// </summary>
    [HttpGet("/api/v1/diagnostic/orders/{idOrCode}/review")]
    [HttpGet("/api/v1/diagnostic-orders/{idOrCode:regex(^(?!review$|receive$|receive-patient$).+)}")]
    [HttpGet("/api/v1/diagnostic-orders/{idOrCode}/review")]
    [HttpGet("/api/v1/diagnostic/workbench/orders/{idOrCode}")]
    [ProducesResponseType<DiagnosticOrderReviewDetailResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticOrderReviewDetailResponseDto>> GetOrderByIdOrCode(
        string idOrCode,
        CancellationToken ct = default)
        => Ok(await _orderReviewService.GetDiagnosticOrderByIdOrCodeAsync(GetUserId(), idOrCode, ct));

    /// <summary>
    /// POST/PUT/PATCH /api/v1/diagnostic/orders/{idOrCode}/confirm — Xác nhận phiếu chỉ định cận lâm sàng đủ điều kiện xử lý (ORDERED -&gt; CONFIRMED) (UC39 / #126).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/confirm")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/confirm")]
    [HttpPatch("/api/v1/diagnostic/orders/{idOrCode}/confirm")]
    [HttpPost("/api/v1/diagnostic/orders/{idOrCode}/review-confirm")]
    [HttpPut("/api/v1/diagnostic/orders/{idOrCode}/review-confirm")]
    [HttpPost("/api/v1/diagnostic-orders/{idOrCode}/confirm")]
    [HttpPut("/api/v1/diagnostic-orders/{idOrCode}/confirm")]
    [HttpPatch("/api/v1/diagnostic-orders/{idOrCode}/confirm")]
    [HttpPost("/api/v1/diagnostic/workbench/orders/{idOrCode}/confirm")]
    [HttpPut("/api/v1/diagnostic/workbench/orders/{idOrCode}/confirm")]
    [ProducesResponseType<ConfirmDiagnosticOrderResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConfirmDiagnosticOrderResponseDto>> ConfirmOrder(
        string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConfirmDiagnosticOrderRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _orderReviewService.ConfirmDiagnosticOrderAsync(
            GetUserId(),
            idOrCode,
            request,
            GetIp(),
            ct));

    /// <summary>
    /// POST/PUT /api/v1/diagnostic/orders/confirm — Xác nhận hàng loạt phiếu chỉ định cận lâm sàng (UC39 / #126).
    /// </summary>
    [HttpPost("/api/v1/diagnostic/orders/confirm")]
    [HttpPut("/api/v1/diagnostic/orders/confirm")]
    [HttpPost("/api/v1/diagnostic-orders/confirm")]
    [HttpPut("/api/v1/diagnostic-orders/confirm")]
    [ProducesResponseType<ConfirmDiagnosticOrderResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConfirmDiagnosticOrderResponseDto>> ConfirmOrdersBatch(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConfirmDiagnosticOrdersBatchRequest? request = null,
        CancellationToken ct = default)
        => Ok(await _orderReviewService.ConfirmDiagnosticOrdersBatchAsync(
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
