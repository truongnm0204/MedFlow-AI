using System.Security.Claims;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/pharmacy/prescriptions")]
[Authorize(Roles = "PHARMACY_STAFF")]
public sealed class PharmacyPrepareMedicinesController(IPrepareMedicinesService service) : ControllerBase
{
    [HttpGet("{idOrCode}/prepare")]
    [HttpGet("/api/v1/pharmacy/prepare/{idOrCode}")]
    [ProducesResponseType<PreparePrescriptionContextDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PreparePrescriptionContextDto>> GetPreparationContext(
        string idOrCode,
        CancellationToken ct)
    {
        var result = await service.GetPreparationContextAsync(GetUserId(), idOrCode, ct, GetIp());
        return Ok(result);
    }

    [HttpPost("{idOrCode}/prepare")]
    [HttpPost("/api/v1/pharmacy/prepare/{idOrCode}")]
    [ProducesResponseType<PreparePrescriptionResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PreparePrescriptionResponseDto>> PreparePrescription(
        string idOrCode,
        [FromBody] PreparePrescriptionRequest request,
        CancellationToken ct)
    {
        var result = await service.PreparePrescriptionAsync(GetUserId(), idOrCode, request, ct, GetIp());
        return Ok(result);
    }

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
