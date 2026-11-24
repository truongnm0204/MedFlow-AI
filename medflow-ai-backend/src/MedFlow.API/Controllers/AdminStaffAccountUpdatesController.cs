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
public sealed class AdminStaffAccountUpdatesController : ControllerBase
{
    private readonly IStaffAccountUpdateService _staffAccountUpdateService;

    public AdminStaffAccountUpdatesController(IStaffAccountUpdateService staffAccountUpdateService)
    {
        _staffAccountUpdateService = staffAccountUpdateService;
    }

    [HttpPut("{staffIdentifier}")]
    [HttpPatch("{staffIdentifier}")]
    [ProducesResponseType<UpdateStaffAccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdateStaffAccountResponse>> Update(
        string staffIdentifier,
        [FromBody] UpdateStaffAccountRequest request,
        CancellationToken ct)
    {
        var result = await _staffAccountUpdateService.UpdateAsync(
            GetUserId(),
            staffIdentifier,
            request,
            GetIpAddress(),
            ct);

        return Ok(result);
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
