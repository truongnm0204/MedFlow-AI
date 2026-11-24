using System.Security.Claims;
using MedFlow.Domain.Exceptions;
using MedFlow.Application.DTOs.ExaminationRooms;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/examination-rooms")]
[Route("api/v1/admin/rooms")]
[Route("api/v1/examination-rooms")]
[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class AdminExaminationRoomsController : ControllerBase
{
    private readonly IExaminationRoomService _examinationRoomService;

    public AdminExaminationRoomsController(IExaminationRoomService examinationRoomService)
    {
        _examinationRoomService = examinationRoomService;
    }

    /// <summary>GET /api/v1/admin/examination-rooms — Lấy danh sách buồng khám và trạng thái khả dụng.</summary>
    [HttpGet]
    [ProducesResponseType<ExaminationRoomListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ExaminationRoomListResponseDto>> GetExaminationRoomList(
        [FromQuery] GetExaminationRoomListRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _examinationRoomService.GetExaminationRoomListAsync(request, ct);
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

    /// <summary>POST /api/v1/admin/examination-rooms — Tạo mới buồng khám / phòng kỹ thuật.</summary>
    [HttpPost]
    [ProducesResponseType<ExaminationRoomItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExaminationRoomItemDto>> CreateExaminationRoom(
        [FromBody] CreateExaminationRoomRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _examinationRoomService.CreateExaminationRoomAsync(request, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy dữ liệu liên quan",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
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
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Xung đột dữ liệu",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    /// <summary>PUT /api/v1/admin/examination-rooms/{id} — Cập nhật thông tin buồng khám / phòng kỹ thuật, sức chứa và trạng thái khả dụng.</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<ExaminationRoomItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExaminationRoomItemDto>> UpdateExaminationRoom(
        [FromRoute] long id,
        [FromBody] UpdateExaminationRoomRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _examinationRoomService.UpdateExaminationRoomAsync(id, request, ct);
            return Ok(result);
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Không có quyền truy cập",
                Detail = ex.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
        catch (ConflictException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Xung đột dữ liệu",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy dữ liệu liên quan",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
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
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Xung đột dữ liệu",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    /// <summary>GET /api/v1/admin/examination-rooms/{idOrCode}/dependencies — Kiểm tra các ràng buộc phụ thuộc của buồng khám.</summary>
    [HttpGet("{idOrCode}/dependencies")]
    [ProducesResponseType<ExaminationRoomDependencySummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExaminationRoomDependencySummaryDto>> CheckDependencies(
        [FromRoute] string idOrCode,
        CancellationToken ct)
    {
        try
        {
            var result = await _examinationRoomService.CheckExaminationRoomDependenciesAsync(GetUserId(), idOrCode, ct);
            return Ok(result);
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Không có quyền truy cập",
                Detail = ex.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy buồng khám",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
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

    /// <summary>POST|PATCH|PUT /api/v1/admin/examination-rooms/{idOrCode}/deactivate (cùng alias DELETE /api/v1/admin/examination-rooms/{idOrCode}) — Ngừng hoạt động buồng khám (UC48 / #180).</summary>
    [HttpPost("{idOrCode}/deactivate")]
    [HttpPatch("{idOrCode}/deactivate")]
    [HttpPut("{idOrCode}/deactivate")]
    [HttpDelete("{idOrCode}")]
    [ProducesResponseType<DeactivateExaminationRoomResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeactivateExaminationRoomResponseDto>> DeactivateExaminationRoom(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeactivateExaminationRoomRequest? request = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _examinationRoomService.DeactivateExaminationRoomAsync(
                GetUserId(),
                idOrCode,
                request ?? new DeactivateExaminationRoomRequest(),
                GetIp(),
                ct);
            return Ok(result);
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Không có quyền thao tác",
                Detail = ex.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
        catch (ConflictException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Xung đột ràng buộc dữ liệu",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy buồng khám",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
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
    }

    /// <summary>POST|PATCH|PUT /api/v1/admin/examination-rooms/{idOrCode}/reactivate — Kích hoạt lại buồng khám.</summary>
    [HttpPost("{idOrCode}/reactivate")]
    [HttpPatch("{idOrCode}/reactivate")]
    [HttpPut("{idOrCode}/reactivate")]
    [ProducesResponseType<DeactivateExaminationRoomResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeactivateExaminationRoomResponseDto>> ReactivateExaminationRoom(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReactivateExaminationRoomRequest? request = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _examinationRoomService.ReactivateExaminationRoomAsync(
                GetUserId(),
                idOrCode,
                request ?? new ReactivateExaminationRoomRequest(),
                GetIp(),
                ct);
            return Ok(result);
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Không có quyền thao tác",
                Detail = ex.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
        catch (ConflictException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Xung đột trạng thái",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy buồng khám",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
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
    }

    private long? GetUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("id");
        return long.TryParse(idStr, out var id) ? id : null;
    }

    private string? GetIp()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
