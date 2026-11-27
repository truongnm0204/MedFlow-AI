using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

/// <summary>
/// Clinical Session Recovery authority (PR #859 / Issue #858, F01 Option B).
/// Route roles are the first layer only; <see cref="IClinicalSessionRecoveryService"/> enforces live
/// permission, active staff/doctor, hospital scope, clinical scope, assignment and state version.
/// SYSTEM_ADMIN / PATIENT / DIAGNOSTIC_STAFF / PHARMACY_STAFF are rejected at both layers.
/// </summary>
[ApiController]
[Authorize(Roles = $"{RoleNames.Reception},{RoleNames.Doctor},{RoleNames.HospitalAdmin}")]
public sealed class ClinicalSessionRecoveryController : ControllerBase
{
    private const string ClinicalRoles =
        $"{RoleNames.Reception},{RoleNames.Doctor},{RoleNames.HospitalAdmin}";

    private readonly IClinicalSessionRecoveryService _service;

    public ClinicalSessionRecoveryController(IClinicalSessionRecoveryService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/visits/{idOrCode}/clinical-session-recovery-context — minimal authorized recovery context.</summary>
    [HttpGet("/api/v1/visits/{idOrCode}/clinical-session-recovery-context")]
    [Authorize(Roles = ClinicalRoles)]
    [ProducesResponseType<ClinicalSessionRecoveryContextDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClinicalSessionRecoveryContextDto>> GetContext(
        [FromRoute] string idOrCode,
        CancellationToken ct)
        => Ok(await _service.GetContextAsync(GetUserId(), idOrCode, ct));

    /// <summary>POST /api/v1/visits/{idOrCode}/clinical-session-recovery-requests — Reception or authorized doctor creates a REQUESTED recovery request. No queue/visit mutation.</summary>
    [HttpPost("/api/v1/visits/{idOrCode}/clinical-session-recovery-requests")]
    [Authorize(Roles = ClinicalRoles)]
    [ProducesResponseType<ClinicalSessionRecoveryRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalSessionRecoveryRequestDto>> RequestRecovery(
        [FromRoute] string idOrCode,
        [FromBody] CreateClinicalSessionRecoveryRequestDto? request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "Lỗi xác thực", Detail = "Payload yêu cầu không hợp lệ." });
        }

        return Ok(await _service.RequestRecoveryAsync(GetUserId(), idOrCode, request, GetIp(), ct));
    }

    /// <summary>POST /api/v1/clinical-session-recovery-requests/{id}/grant — DOCTOR + CLINICAL_SESSION_RECOVERY_GRANT issues a 2h single-use grant to a different eligible doctor.</summary>
    [HttpPost("/api/v1/clinical-session-recovery-requests/{id:long}/grant")]
    [Authorize(Roles = ClinicalRoles)]
    [ProducesResponseType<ClinicalSessionRecoveryRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalSessionRecoveryRequestDto>> Grant(
        [FromRoute] long id,
        [FromBody] GrantClinicalSessionRecoveryRequestDto? request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "Lỗi xác thực", Detail = "Payload cấp grant không hợp lệ." });
        }

        return Ok(await _service.GrantAsync(GetUserId(), id, request, GetIp(), ct));
    }

    /// <summary>POST /api/v1/clinical-session-recovery-requests/{id}/reject — qualified physician rejects a request/grant.</summary>
    [HttpPost("/api/v1/clinical-session-recovery-requests/{id:long}/reject")]
    [Authorize(Roles = ClinicalRoles)]
    [ProducesResponseType<ClinicalSessionRecoveryRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalSessionRecoveryRequestDto>> Reject(
        [FromRoute] long id,
        [FromBody] ClinicalSessionRecoveryDecisionRequestDto? request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "Lỗi xác thực", Detail = "Payload từ chối không hợp lệ." });
        }

        return Ok(await _service.RejectAsync(GetUserId(), id, request, GetIp(), ct));
    }

    /// <summary>POST /api/v1/clinical-session-recovery-requests/{id}/revoke — grant issuer revokes an active grant.</summary>
    [HttpPost("/api/v1/clinical-session-recovery-requests/{id:long}/revoke")]
    [Authorize(Roles = ClinicalRoles)]
    [ProducesResponseType<ClinicalSessionRecoveryRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalSessionRecoveryRequestDto>> Revoke(
        [FromRoute] long id,
        [FromBody] ClinicalSessionRecoveryDecisionRequestDto? request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "Lỗi xác thực", Detail = "Payload thu hồi không hợp lệ." });
        }

        return Ok(await _service.RevokeAsync(GetUserId(), id, request, GetIp(), ct));
    }

    /// <summary>POST /api/v1/clinical-session-recovery-requests/{id}/cancel — requester/Reception withdraws an ungranted request.</summary>
    [HttpPost("/api/v1/clinical-session-recovery-requests/{id:long}/cancel")]
    [Authorize(Roles = ClinicalRoles)]
    [ProducesResponseType<ClinicalSessionRecoveryRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalSessionRecoveryRequestDto>> Cancel(
        [FromRoute] long id,
        [FromBody] ClinicalSessionRecoveryDecisionRequestDto? request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "Lỗi xác thực", Detail = "Payload rút yêu cầu không hợp lệ." });
        }

        return Ok(await _service.CancelAsync(GetUserId(), id, request, GetIp(), ct));
    }

    /// <summary>POST /api/v1/clinical-session-recovery-requests/{id}/confirm — assigned doctor (own session) or delegated grant recipient atomically releases the ticket.</summary>
    [HttpPost("/api/v1/clinical-session-recovery-requests/{id:long}/confirm")]
    [Authorize(Roles = ClinicalRoles)]
    [ProducesResponseType<ClinicalSessionRecoveryConfirmResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalSessionRecoveryConfirmResponseDto>> Confirm(
        [FromRoute] long id,
        [FromBody] ConfirmClinicalSessionRecoveryRequestDto? request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "Lỗi xác thực", Detail = "Payload xác nhận phục hồi không hợp lệ." });
        }

        return Ok(await _service.ConfirmAsync(GetUserId(), id, request, GetIp(), ct));
    }

    /// <summary>GET /api/v1/clinical-session-recovery-requests — PM-approved scoped list (Doctor grant/confirm scope, Reception/HospitalAdmin hospital scope).</summary>
    [HttpGet("/api/v1/clinical-session-recovery-requests")]
    [Authorize(Roles = ClinicalRoles)]
    [ProducesResponseType<IReadOnlyList<ClinicalSessionRecoveryListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ClinicalSessionRecoveryListItemDto>>> ListRequests(CancellationToken ct)
        => Ok(await _service.ListRequestsAsync(GetUserId(), ct));

    /// <summary>GET /api/v1/clinical-session-recovery-requests/{id}/eligible-recipients — active doctors with RECOVER + same hospital/clinical scope.</summary>
    [HttpGet("/api/v1/clinical-session-recovery-requests/{id:long}/eligible-recipients")]
    [Authorize(Roles = ClinicalRoles)]
    [ProducesResponseType<IReadOnlyList<ClinicalSessionRecoveryEligibleRecipientDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<ClinicalSessionRecoveryEligibleRecipientDto>>> GetEligibleRecipients(
        [FromRoute] long id,
        CancellationToken ct)
        => Ok(await _service.GetEligibleRecipientsAsync(GetUserId(), id, ct));

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
