using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/examination-services")]
[Route("api/v1/admin/services")]
[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class AdminExaminationServicesController : ControllerBase
{
    private readonly IExaminationServiceAdminService _service;

    public AdminExaminationServicesController(IExaminationServiceAdminService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/admin/examination-services — Lấy danh sách dịch vụ khám dành cho Hospital Admin quản trị cơ sở.</summary>
    [HttpGet]
    [ProducesResponseType<ExaminationServiceAdminListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ExaminationServiceAdminListResponseDto>> GetExaminationServiceList(
        [FromQuery] GetExaminationServiceListAdminRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.GetExaminationServiceListAsync(request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Yêu cầu không hợp lệ",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }
}
