using System.Security.Claims;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/pharmacy/prescriptions")]
[Authorize(Roles = RoleNames.PharmacyStaff)]
public sealed class PharmacyFefoBatchesController(IFefoBatchSelectionService service) : ControllerBase
{
    /// <summary>GET /api/v1/pharmacy/prescriptions/{idOrCode}/fefo-batches — Danh sách lô khả dụng theo nguyên tắc FEFO (UC45 / #146).</summary>
    [HttpGet("{idOrCode}/fefo-batches")]
    [HttpGet("/api/v1/pharmacy/fefo-batches/{idOrCode}")]
    [ProducesResponseType<FefoBatchSelectionContextDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FefoBatchSelectionContextDto>> GetFefoBatches(
        [FromRoute] string idOrCode,
        [FromQuery] bool includeUnavailable = true,
        CancellationToken ct = default)
        => Ok(await service.GetFefoBatchesAsync(RequireUserId(), idOrCode, includeUnavailable, ct, GetIp()));

    /// <summary>POST|PUT /api/v1/pharmacy/prescriptions/{idOrCode}/fefo-batches — Xác thực &amp; lưu lựa chọn lô thuốc theo chuẩn FEFO (UC45 / #146).</summary>
    [HttpPost("{idOrCode}/fefo-batches")]
    [HttpPut("{idOrCode}/fefo-batches")]
    [HttpPost("/api/v1/pharmacy/fefo-batches/{idOrCode}")]
    [HttpPut("/api/v1/pharmacy/fefo-batches/{idOrCode}")]
    [ProducesResponseType<SelectFefoBatchesResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SelectFefoBatchesResponseDto>> SelectFefoBatches(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SelectFefoBatchesRequest? request,
        CancellationToken ct = default)
        => Ok(await service.SelectFefoBatchesAsync(RequireUserId(), idOrCode, request, ct, GetIp()));

    private long RequireUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!long.TryParse(sub, out var userId) || userId <= 0)
        {
            throw new UnauthorizedAccessException("Token không hợp lệ.");
        }

        return userId;
    }

    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();
}
