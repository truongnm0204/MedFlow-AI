using System.Security.Claims;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/service-prices")]
[Authorize(Roles = $"{RoleNames.HospitalAdmin},{RoleNames.SystemAdmin}")]
public sealed class AdminServicePricesController(IServicePriceAdminService service) : ControllerBase
{
    /// <summary>GET /api/v1/admin/service-prices — Danh sách giá dịch vụ hiện hành và lịch sử thay đổi theo cơ sở y tế.</summary>
    [HttpGet]
    [ProducesResponseType<ServicePriceAdminListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServicePriceAdminListResponseDto>> GetServicePrices(
        [FromQuery] GetServicePriceListAdminRequest request,
        CancellationToken ct)
        => Ok(await service.GetServicePriceListAsync(RequireUserId(), request, ct));

    /// <summary>GET /api/v1/admin/examination-services/{idOrCode}/prices — Chi tiết giá hiện hành &amp; lịch sử giá của một dịch vụ.</summary>
    [HttpGet("/api/v1/admin/examination-services/{idOrCode}/prices")]
    [HttpGet("/api/v1/admin/services/{idOrCode}/prices")]
    [HttpGet("{idOrCode}")]
    [ProducesResponseType<ServicePriceDetailResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServicePriceDetailResponseDto>> GetServicePriceDetail(
        [FromRoute] string idOrCode,
        [FromQuery] long? hospitalId = null,
        CancellationToken ct = default)
        => Ok(await service.GetServicePriceDetailAsync(RequireUserId(), idOrCode, hospitalId, ct));

    /// <summary>POST|PUT /api/v1/admin/examination-services/{idOrCode}/prices — Cập nhật biểu phí dịch vụ (UC52 / #208).</summary>
    [HttpPost("/api/v1/admin/examination-services/{idOrCode}/prices")]
    [HttpPut("/api/v1/admin/examination-services/{idOrCode}/prices")]
    [HttpPost("/api/v1/admin/services/{idOrCode}/prices")]
    [HttpPut("/api/v1/admin/services/{idOrCode}/prices")]
    [ProducesResponseType<ServicePriceAdminItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServicePriceAdminItemDto>> UpdateServicePriceByRoute(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateServicePriceAdminRequest? request,
        CancellationToken ct = default)
        => Ok(await service.UpdateServicePriceAsync(RequireUserId(), idOrCode, request, GetIp(), ct));

    /// <summary>POST|PUT /api/v1/admin/service-prices — Cập nhật biểu phí dịch vụ qua body.</summary>
    [HttpPost]
    [HttpPut]
    [ProducesResponseType<ServicePriceAdminItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServicePriceAdminItemDto>> UpdateServicePriceFromBody(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateServicePriceAdminRequest? request,
        CancellationToken ct = default)
        => Ok(await service.UpdateServicePriceAsync(RequireUserId(), null, request, GetIp(), ct));

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
