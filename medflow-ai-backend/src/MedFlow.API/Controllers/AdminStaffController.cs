using System.Security.Claims;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/staff")]
[Route("api/v1/system-admin/staff")]
public sealed class AdminStaffController : ControllerBase
{
    private readonly IStaffAccountListService _staffAccountListService;
    private readonly IStaffAccountAdminService _staffAccountAdminService;

    public AdminStaffController(
        IStaffAccountListService staffAccountListService,
        IStaffAccountAdminService staffAccountAdminService)
    {
        _staffAccountListService = staffAccountListService;
        _staffAccountAdminService = staffAccountAdminService;
    }

    /// <summary>GET /api/v1/admin/staff — Lấy danh sách tài khoản nhân sự theo phạm vi quản trị.</summary>
    [HttpGet]
    [Authorize(Roles = "SYSTEM_ADMIN,HOSPITAL_ADMIN")]
    [ProducesResponseType<StaffAccountListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffAccountListResponseDto>> GetStaffAccounts(
        [FromQuery] GetStaffAccountListRequest request,
        CancellationToken ct)
    {
        var result = await _staffAccountListService.GetStaffAccountsAsync(GetUserId(), request, ct);
        return Ok(result);
    }

    /// <summary>PATCH /api/v1/admin/staff/{id}/deactivate — Vô hiệu hóa tài khoản nhân viên.</summary>
    [HttpPatch("{id:long}/deactivate")]
    [Authorize(Roles = "SYSTEM_ADMIN")]
    [ProducesResponseType<DeactivateStaffAccountResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeactivateStaffAccountResponseDto>> Deactivate(
        long id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeactivateStaffAccountRequest? request,
        CancellationToken ct)
        => Ok(await _staffAccountAdminService.DeactivateAsync(GetUserId(), id, request, GetIp(), ct));

    private string? GetIp()
        => HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
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
