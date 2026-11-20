using System.Security.Claims;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/reception/patient-identity-claims")]
[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class PatientIdentityClaimsController : ControllerBase
{
    private readonly IPatientIdentityClaimService _service;

    public PatientIdentityClaimsController(IPatientIdentityClaimService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/reception/patient-identity-claims — Hàng đợi xác minh.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StaffIdentityClaimDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<StaffIdentityClaimDto>>> GetQueue(
        [FromQuery] string? status,
        CancellationToken ct)
        => Ok(await _service.GetReviewQueueAsync(GetUserId(), status, ct));

    /// <summary>POST /api/v1/reception/patient-identity-claims/{id}/approve</summary>
    [HttpPost("{id:long}/approve")]
    [ProducesResponseType<IdentityClaimResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IdentityClaimResponse>> Approve(
        [FromRoute] long id,
        CancellationToken ct)
        => Ok(await _service.ApproveAsync(GetUserId(), id, ct));

    /// <summary>POST /api/v1/reception/patient-identity-claims/{id}/reject</summary>
    [HttpPost("{id:long}/reject")]
    [ProducesResponseType<IdentityClaimResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IdentityClaimResponse>> Reject(
        [FromRoute] long id,
        [FromBody] RejectIdentityClaimRequest request,
        CancellationToken ct)
        => Ok(await _service.RejectAsync(GetUserId(), id, request, ct));

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
            throw new UnauthorizedAccessException("Token không hợp lệ");

        return userId;
    }
}
