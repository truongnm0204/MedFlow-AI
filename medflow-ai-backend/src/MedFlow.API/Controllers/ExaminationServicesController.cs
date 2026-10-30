using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Services.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/examination-services")]
public class ExaminationServicesController : ControllerBase
{
    private readonly IExaminationServiceCatalogService _catalogService;

    public ExaminationServicesController(IExaminationServiceCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    /// <summary>GET /api/v1/examination-services — Tìm kiếm, lọc và phân trang danh mục dịch vụ khám ngoại trú.</summary>
    [HttpGet]
    public async Task<ActionResult<ServiceSearchResponseDto>> Search(
        [FromQuery] SearchExaminationServicesRequest request,
        CancellationToken ct)
        => Ok(await _catalogService.SearchAsync(request, ct));

    /// <summary>GET /api/v1/examination-services/{idOrCode} — Xem chi tiết dịch vụ khám theo ID hoặc mã dịch vụ.</summary>
    [HttpGet("{idOrCode}")]
    public async Task<ActionResult<ExaminationServiceDto>> GetByIdOrCode(
        [FromRoute] string idOrCode,
        CancellationToken ct)
        => Ok(await _catalogService.GetByIdOrCodeAsync(idOrCode, ct));
}
