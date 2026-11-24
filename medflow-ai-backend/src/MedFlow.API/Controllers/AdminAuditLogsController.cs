using System.Security.Claims;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/audit-logs")]
[Route("api/v1/system-admin/audit-logs")]
[Authorize(Roles = $"{RoleNames.SystemAdmin},{RoleNames.HospitalAdmin}")]
public sealed class AdminAuditLogsController : ControllerBase
{
    private readonly IAuditLogQueryService _auditLogQueryService;

    public AdminAuditLogsController(IAuditLogQueryService auditLogQueryService)
    {
        _auditLogQueryService = auditLogQueryService;
    }

    /// <summary>
    /// GET /api/v1/admin/audit-logs — Tra cứu &amp; lọc nhật ký kiểm toán theo phạm vi quản trị (UC60 / ADR-0054).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<AuditLogListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuditLogListResponseDto>> GetAuditLogs(
        [FromQuery] GetAuditLogsRequest request,
        CancellationToken ct)
    {
        var result = await _auditLogQueryService.GetAuditLogsAsync(GetUserId(), request, ct);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/v1/admin/audit-logs/{id} — Xem chi tiết sự kiện kiểm toán và metadata đã che thông tin nhạy cảm (UC60 / ADR-0054).
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<AuditLogDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuditLogDetailDto>> GetAuditLogById(
        long id,
        CancellationToken ct)
    {
        var result = await _auditLogQueryService.GetAuditLogByIdAsync(GetUserId(), id, ct);
        return Ok(result);
    }

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
