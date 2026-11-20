using System.Security.Claims;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/staff")]
[Route("api/v1/system-admin/staff")]
[Authorize(Roles = "SYSTEM_ADMIN,HOSPITAL_ADMIN")]
public sealed class AdminStaffController : ControllerBase
{
    private readonly IStaffAccountListService _staffAccountListService;

    public AdminStaffController(IStaffAccountListService staffAccountListService)
    {
        _staffAccountListService = staffAccountListService;
    }

    /// <summary>GET /api/v1/admin/staff — Lấy danh sách tài khoản nhân sự theo phạm vi quản trị.</summary>
    [HttpGet]
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
