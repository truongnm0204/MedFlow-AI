using System.Security.Claims;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = $"{RoleNames.Patient},{RoleNames.Doctor},{RoleNames.Reception},{RoleNames.DiagnosticStaff},{RoleNames.HospitalAdmin},{RoleNames.HeadDoctor},{RoleNames.ClinicalNurse}")]
public sealed class VisitDiagnosticAssignmentsController : ControllerBase
{
    private readonly IDiagnosticOrderRoutingService _routingService;

    public VisitDiagnosticAssignmentsController(IDiagnosticOrderRoutingService routingService)
    {
        _routingService = routingService;
    }

    /// <summary>
    /// GET /api/v1/visits/{idOrCode}/diagnostic-order-assignments — Dữ liệu phiếu chỉ định CLS (#871):
    /// dịch vụ, điểm được phân công, thứ tự. Bệnh nhân (chủ sở hữu) xem read-only cùng endpoint.
    /// </summary>
    [HttpGet("/api/v1/visits/{idOrCode}/diagnostic-order-assignments")]
    [HttpGet("/api/v1/patients/visits/{idOrCode}/diagnostic-order-assignments")]
    [HttpGet("/api/v1/doctor/visits/{idOrCode}/diagnostic-order-assignments")]
    [HttpGet("/api/v1/reception/visits/{idOrCode}/diagnostic-order-assignments")]
    [ProducesResponseType<DiagnosticOrderAssignmentSlipDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiagnosticOrderAssignmentSlipDto>> GetAssignments(
        [FromRoute] string idOrCode,
        CancellationToken ct)
        => Ok(await _routingService.GetVisitAssignmentsAsync(GetUserId(), idOrCode, ct));

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
}
