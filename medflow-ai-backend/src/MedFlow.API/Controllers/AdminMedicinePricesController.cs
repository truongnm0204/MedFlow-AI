using System.Security.Claims;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/medicine-prices")]
[Authorize(Roles = $"{RoleNames.HospitalAdmin},{RoleNames.SystemAdmin}")]
public sealed class AdminMedicinePricesController(IMedicinePriceAdminService service) : ControllerBase
{
    /// <summary>GET /api/v1/admin/medicine-prices — Danh sách giá thuốc hiện hành theo cơ sở y tế.</summary>
    [HttpGet]
    [ProducesResponseType<MedicinePriceAdminListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MedicinePriceAdminListResponseDto>> GetMedicinePrices(
        [FromQuery] GetMedicinePriceListAdminRequest request,
        CancellationToken ct)
        => Ok(await service.GetMedicinePriceListAsync(RequireUserId(), request, ct));

    /// <summary>GET /api/v1/admin/medicines/{idOrCode}/prices — Giá hiện hành &amp; lịch sử giá của một thuốc.</summary>
    [HttpGet("/api/v1/admin/medicines/{idOrCode}/prices")]
    [HttpGet("{idOrCode}")]
    [ProducesResponseType<MedicinePriceDetailResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MedicinePriceDetailResponseDto>> GetMedicinePriceDetail(
        [FromRoute] string idOrCode,
        [FromQuery] long? hospitalId = null,
        CancellationToken ct = default)
        => Ok(await service.GetMedicinePriceDetailAsync(RequireUserId(), idOrCode, hospitalId, ct));

    /// <summary>GET /api/v1/admin/medicines/{idOrCode}/prices/history — Lịch sử các phiên bản giá theo thời gian.</summary>
    [HttpGet("/api/v1/admin/medicines/{idOrCode}/prices/history")]
    [HttpGet("/api/v1/admin/medicine-prices/{idOrCode}/history")]
    [ProducesResponseType<MedicinePriceHistoryResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MedicinePriceHistoryResponseDto>> GetMedicinePriceHistory(
        [FromRoute] string idOrCode,
        [FromQuery] long? hospitalId = null,
        CancellationToken ct = default)
        => Ok(await service.GetMedicinePriceHistoryAsync(RequireUserId(), idOrCode, hospitalId, ct));

    /// <summary>POST|PUT /api/v1/admin/medicines/{idOrCode}/prices — Thiết lập giá thuốc theo hiệu lực (#865).</summary>
    [HttpPost("/api/v1/admin/medicines/{idOrCode}/prices")]
    [HttpPut("/api/v1/admin/medicines/{idOrCode}/prices")]
    [ProducesResponseType<MedicinePriceAdminItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MedicinePriceAdminItemDto>> UpdateMedicinePriceByRoute(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateMedicinePriceAdminRequest? request,
        CancellationToken ct = default)
        => Ok(await service.UpdateMedicinePriceAsync(RequireUserId(), idOrCode, request, GetIp(), ct));

    /// <summary>POST|PUT /api/v1/admin/medicine-prices — Thiết lập giá thuốc qua body.</summary>
    [HttpPost]
    [HttpPut]
    [ProducesResponseType<MedicinePriceAdminItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MedicinePriceAdminItemDto>> UpdateMedicinePriceFromBody(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateMedicinePriceAdminRequest? request,
        CancellationToken ct = default)
        => Ok(await service.UpdateMedicinePriceAsync(RequireUserId(), null, request, GetIp(), ct));

    private long RequireUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
        {
            throw new UnauthorizedAccessException("Token không hợp lệ.");
        }

        return userId;
    }

    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();
}
