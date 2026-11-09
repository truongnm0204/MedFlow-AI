using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/reception/patients/search")]
[Route("api/v1/reception/patients")]
[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
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
        var result = await _searchService.SearchPatientsAsync(request, ct);
        return Ok(result);
    }
}
