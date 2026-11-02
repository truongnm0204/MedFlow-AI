using MedFlow.Application.DTOs.ExaminationSlots;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/examination-slots")]
[Route("api/v1/admin/slots")]
[Route("api/v1/examination-slots")]
[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class AdminExaminationSlotsController : ControllerBase
{
    private readonly IExaminationSlotAdminService _service;

    public AdminExaminationSlotsController(IExaminationSlotAdminService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/admin/examination-slots — Lấy danh sách khung giờ khám, ca khám bệnh và thống kê capacity.</summary>
    [HttpGet]
    [ProducesResponseType<ExaminationSlotAdminListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ExaminationSlotAdminListResponseDto>> GetExaminationSlotList(
        [FromQuery] GetExaminationSlotListRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.GetExaminationSlotListAsync(request, ct);
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

