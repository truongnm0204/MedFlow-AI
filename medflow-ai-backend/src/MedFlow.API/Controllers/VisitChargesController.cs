using System.Security.Claims;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "PATIENT,RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class VisitChargesController : ControllerBase
{
    private readonly IVisitChargesService _service;

    public VisitChargesController(IVisitChargesService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/visits/{idOrCode}/charges — Xem khoản phí và trạng thái thanh toán của lượt khám (UC18 / #160).</summary>
    [HttpGet("/api/v1/visits/{idOrCode}/charges")]
    [HttpGet("/api/v1/reception/visits/{idOrCode}/charges")]
    [HttpGet("/api/v1/patients/visits/{idOrCode}/charges")]
    [ProducesResponseType<VisitChargesResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitChargesResponseDto>> GetCharges(
        [FromRoute] string idOrCode,
        CancellationToken ct)
        => Ok(await _service.GetAsync(GetUserId(), idOrCode, ct));

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Token không hợp lệ");

        return userId;
    }
}
