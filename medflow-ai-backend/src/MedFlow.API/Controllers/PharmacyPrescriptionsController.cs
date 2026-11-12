using System.Security.Claims;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/pharmacy/prescriptions")]
[Authorize(Roles = "PHARMACY_STAFF")]
public sealed class PharmacyPrescriptionsController(IPrescriptionSearchService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PrescriptionSearchResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PrescriptionSearchResponseDto>> Search(
        [FromQuery] GetPrescriptionSearchRequest request, CancellationToken ct)
        => Ok(await service.SearchAsync(GetUserId(), request, ct, GetIp()));

    [HttpGet("{idOrCode}")]
    [ProducesResponseType<PrescriptionSearchDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PrescriptionSearchDetailDto>> GetById(
        string idOrCode, CancellationToken ct)
        => Ok(await service.GetByIdAsync(GetUserId(), idOrCode, ct, GetIp()));

    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!long.TryParse(sub, out var userId) || userId <= 0)
        {
            throw new UnauthorizedAccessException("Token không hợp lệ");
        }

        return userId;
    }
}
