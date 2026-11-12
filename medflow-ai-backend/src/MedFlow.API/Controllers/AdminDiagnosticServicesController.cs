using System.Security.Claims;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/diagnostic-services")]
[Authorize(Roles = "HOSPITAL_ADMIN")]
public sealed class AdminDiagnosticServicesController : ControllerBase
{
    private readonly IDiagnosticServiceAdminService _service;

    public AdminDiagnosticServicesController(IDiagnosticServiceAdminService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/admin/diagnostic-services — Danh sách dịch vụ cận lâm sàng thuộc cơ sở của Hospital Admin.</summary>
    [HttpGet]
    [ProducesResponseType<DiagnosticServiceAdminListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticServiceAdminListResponseDto>> GetDiagnosticServiceList(
        [FromQuery] GetDiagnosticServiceListAdminRequest request,
        CancellationToken ct)
        => Ok(await _service.GetDiagnosticServiceListAsync(GetUserId(), request, ct));

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
