using System.Security.Claims;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/integrations")]
[Route("api/v1/system-admin/integrations")]
[Authorize(Roles = $"{RoleNames.SystemAdmin},{RoleNames.HospitalAdmin}")]
public sealed class AdminIntegrationsController : ControllerBase
{
    private readonly IIntegrationConfigService _integrationConfigService;

    public AdminIntegrationsController(IIntegrationConfigService integrationConfigService)
    {
        _integrationConfigService = integrationConfigService;
    }

    /// <summary>
    /// GET /api/v1/admin/integrations — Xem danh sách cấu hình tích hợp dịch vụ bên ngoài theo cơ sở (UC59 / ADR-0054).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IntegrationConfigListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IntegrationConfigListResponseDto>> GetIntegrations(
        [FromQuery] GetIntegrationsRequest request,
        CancellationToken ct)
    {
        var result = await _integrationConfigService.GetIntegrationsAsync(GetUserId(), request, ct);
        return Ok(result);
    }

    /// <summary>
    /// PUT /api/v1/admin/integrations/{providerKey} — Cập nhật cấu hình tích hợp theo cơ sở và ghi nhật ký kiểm toán (UC59 / ADR-0054).
    /// </summary>
    [HttpPut("{providerKey}")]
    [ProducesResponseType<IntegrationProviderItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IntegrationProviderItemDto>> UpdateIntegration(
        string providerKey,
        [FromBody] UpdateIntegrationConfigRequest request,
        CancellationToken ct)
    {
        var result = await _integrationConfigService.UpdateIntegrationAsync(
            GetUserId(),
            providerKey,
            request,
            GetIp(),
            ct);
        return Ok(result);
    }

    private string? GetIp()
        => HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
           ?? HttpContext.Connection.RemoteIpAddress?.ToString();

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
        {
            throw new UnauthorizedAccessException("Token không hợp lệ");
        }

        return userId;
    }
}
