using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize]
public sealed class ReceptionReturnCoordinationController : ControllerBase
{
    private readonly IReturnCoordinationService _returnCoordinationService;

    public ReceptionReturnCoordinationController(IReturnCoordinationService returnCoordinationService)
    {
        _returnCoordinationService = returnCoordinationService;
    }

    /// <summary>
    /// GET /api/v1/reception/visits/{idOrCode}/return-coordination-context — Ngữ cảnh điều phối khám kết luận / đánh giá lại (Stage E2 / RC-04..09).
    /// </summary>
    [HttpGet("/api/v1/reception/visits/{idOrCode}/return-coordination-context")]
    [ProducesResponseType<ReturnCoordinationContextDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReturnCoordinationContextDto>> GetReturnCoordinationContext(
        [FromRoute] string idOrCode,
        CancellationToken ct = default)
        => Ok(await _returnCoordinationService.GetReturnCoordinationContextAsync(GetUserId(), idOrCode, ct));

    /// <summary>
    /// POST /api/v1/reception/visits/{idOrCode}/return-enrollment — Điều phối bác sĩ và cấp số khám kết luận / đánh giá lại (Stage E2 / RC-01..22).
    /// </summary>
    [HttpPost("/api/v1/reception/visits/{idOrCode}/return-enrollment")]
    [Authorize(Roles = $"{RoleNames.Reception},{RoleNames.HospitalAdmin}")]
    [ProducesResponseType<ReturnEnrollmentResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReturnEnrollmentResponseDto>> EnrollReturnConsultation(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateReturnEnrollmentRequestDto? request = null,
        CancellationToken ct = default)
        => Ok(await _returnCoordinationService.EnrollReturnConsultationAsync(GetUserId(), idOrCode, request, GetIp(), ct));

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
