using System.Security.Claims;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/staff-accounts")]
[Route("api/v1/system-admin/staff-accounts")]
[Authorize(Roles = RoleNames.SystemAdmin)]
public sealed class AdminStaffAccountsController : ControllerBase
{
    private readonly IStaffAccountService _staffAccountService;

    public AdminStaffAccountsController(IStaffAccountService staffAccountService)
    {
        _staffAccountService = staffAccountService;
    }

    [HttpPost]
    [ProducesResponseType<CreateStaffAccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateStaffAccountResponse>> Create(
        [FromBody] CreateStaffAccountRequest request,
        CancellationToken ct)
    {
        var result = await _staffAccountService.CreateAsync(
            GetUserId(),
            request,
            GetIpAddress(),
            ct);

        return Created($"/api/v1/admin/staff-accounts/{result.StaffId}", result);
    }

    private long GetUserId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(subject) || !long.TryParse(subject, out var userId))
        {
            throw new UnauthorizedAccessException("Token không hợp lệ.");
        }

        return userId;
    }

    private string? GetIpAddress() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();
}
