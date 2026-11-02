using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Services.Doctors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/doctors")]
[AllowAnonymous]
public sealed class DoctorsController : ControllerBase
{
    private readonly IDoctorCatalogService _doctorCatalogService;

    public DoctorsController(IDoctorCatalogService doctorCatalogService)
    {
        _doctorCatalogService = doctorCatalogService;
    }

    /// <summary>GET /api/v1/doctors — Tìm kiếm, lọc và phân trang danh bạ bác sĩ có thể đặt lịch.</summary>
    [HttpGet]
    [ProducesResponseType<DoctorSearchResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DoctorSearchResponseDto>> Search([FromQuery] SearchDoctorsRequest request, CancellationToken ct)
        => Ok(await _doctorCatalogService.SearchAsync(request, ct));

    /// <summary>GET /api/v1/doctors/{idOrCode} — Xem chi tiết hồ sơ chuyên môn bác sĩ theo ID hoặc mã bác sĩ.</summary>
    [HttpGet("{idOrCode}")]
    [ProducesResponseType<DoctorDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DoctorDetailDto>> GetByIdOrCode(
        [FromRoute] string idOrCode,
        CancellationToken ct)
        => Ok(await _doctorCatalogService.GetByIdOrCodeAsync(idOrCode, ct));
}
