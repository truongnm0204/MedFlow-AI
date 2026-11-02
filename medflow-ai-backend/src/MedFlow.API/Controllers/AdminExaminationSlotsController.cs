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

    /// <summary>POST /api/v1/admin/examination-slots — Tạo mới khung giờ khám (examination slot).</summary>
    [HttpPost]
    [ProducesResponseType<ExaminationSlotAdminItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExaminationSlotAdminItemDto>> CreateExaminationSlot(
        [FromBody] CreateExaminationSlotRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.CreateExaminationSlotAsync(request, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Dữ liệu không hợp lệ",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy tài nguyên",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (InvalidOperationException ex) when (!ex.Message.Contains("transient failure", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Xung đột lịch khám",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    /// <summary>PUT /api/v1/admin/examination-slots/{id} — Cập nhật thông tin khung giờ khám, sức chứa và trạng thái hoạt động.</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<ExaminationSlotAdminItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExaminationSlotAdminItemDto>> UpdateExaminationSlot(
        [FromRoute] long id,
        [FromBody] UpdateExaminationSlotRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.UpdateExaminationSlotAsync(id, request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Dữ liệu không hợp lệ",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy tài nguyên",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (InvalidOperationException ex) when (!ex.Message.Contains("transient failure", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Xung đột lịch khám",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }
}

