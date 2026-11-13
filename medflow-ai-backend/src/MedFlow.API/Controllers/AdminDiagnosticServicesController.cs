using System.Security.Claims;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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

    /// <summary>GET /api/v1/admin/diagnostic-services/form-options — Khoa và chuyên khoa đang hoạt động để tạo/sửa dịch vụ.</summary>
    [HttpGet("form-options")]
    [ProducesResponseType<DiagnosticServiceFormOptionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiagnosticServiceFormOptionsDto>> GetFormOptions(CancellationToken ct)
        => Ok(await _service.GetFormOptionsAsync(GetUserId(), ct));

    /// <summary>POST /api/v1/admin/diagnostic-services — Tạo dịch vụ cận lâm sàng.</summary>
    [HttpPost]
    [ProducesResponseType<DiagnosticServiceAdminItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiagnosticServiceAdminItemDto>> CreateDiagnosticService(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateDiagnosticServiceAdminRequest? request,
        CancellationToken ct)
        => StatusCode(
            StatusCodes.Status201Created,
            await _service.CreateDiagnosticServiceAsync(GetUserId(), request, GetIp(), ct));

    /// <summary>PUT /api/v1/admin/diagnostic-services/{id} — Cập nhật dịch vụ cận lâm sàng. Không đổi mã và giá.</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<DiagnosticServiceAdminItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiagnosticServiceAdminItemDto>> UpdateDiagnosticService(
        long id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateDiagnosticServiceAdminRequest? request,
        CancellationToken ct)
        => Ok(await _service.UpdateDiagnosticServiceAsync(GetUserId(), id, request, GetIp(), ct));

    /// <summary>PATCH /api/v1/admin/diagnostic-services/{id}/deactivate — Ngừng hoạt động dịch vụ cận lâm sàng.</summary>
    [HttpPatch("{id:long}/deactivate")]
    [ProducesResponseType<DiagnosticServiceAdminItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiagnosticServiceAdminItemDto>> DeactivateDiagnosticService(
        long id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeactivateDiagnosticServiceAdminRequest? request,
        CancellationToken ct)
        => Ok(await _service.DeactivateDiagnosticServiceAsync(GetUserId(), id, request, GetIp(), ct));

    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();

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
