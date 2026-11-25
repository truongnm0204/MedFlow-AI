using System.Security.Claims;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/reception/patients/search")]
[Route("api/v1/reception/patients")]
[Authorize(Roles = $"{RoleNames.Reception},{RoleNames.HospitalAdmin},{RoleNames.SystemAdmin}")]
public sealed class ReceptionPatientSearchController : ControllerBase
{
    private readonly IReceptionPatientSearchService _searchService;

    public ReceptionPatientSearchController(IReceptionPatientSearchService searchService)
    {
        _searchService = searchService;
    }

    /// <summary>
    /// GET /api/v1/reception/patients/search — Tìm kiếm bệnh nhân / lịch hẹn trong ngày cho lễ tân.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SearchPatientsResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SearchPatientsResponseDto>> Search(
        [FromQuery] SearchPatientsRequest request,
        CancellationToken ct)
    {
        var result = await _searchService.SearchPatientsAsync(request, ct, GetUserId());
        return Ok(result);
    }

    /// <summary>
    /// GET /api/v1/reception/dashboard-summary — Thống kê thực tế trong ngày trên Tổng quan Quầy Tiếp đón (#12).
    /// </summary>
    [HttpGet("/api/v1/reception/dashboard-summary")]
    [ProducesResponseType<ReceptionDashboardSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReceptionDashboardSummaryDto>> GetDashboardSummary(CancellationToken ct)
        => Ok(await _searchService.GetDashboardSummaryAsync(GetUserId(), ct));

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
            throw new UnauthorizedAccessException("Token không hợp lệ");

        return userId;
    }
}
