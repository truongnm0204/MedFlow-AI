using System.Security.Claims;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Services.Doctors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/doctors")]
[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class AdminDoctorsController : ControllerBase
{
    private readonly IDoctorAdminService _doctorAdminService;

    public AdminDoctorsController(IDoctorAdminService doctorAdminService)
    {
        _doctorAdminService = doctorAdminService;
    }

    /// <summary>GET /api/v1/admin/doctors — Lấy danh sách bác sĩ quản trị với tìm kiếm, lọc theo khoa phòng/chuyên khoa/trạng thái và phân trang.</summary>
    [HttpGet]
    [ProducesResponseType<DoctorAdminListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DoctorAdminListResponseDto>> GetDoctorList(
        [FromQuery] GetDoctorListAdminRequest request,
        CancellationToken ct)
    {
        var result = await _doctorAdminService.GetDoctorListAsync(request, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/admin/doctors — Tạo mới hồ sơ bác sĩ dựa trên nhân sự (Staff) hợp lệ.</summary>
    [HttpPost]
    [ProducesResponseType<DoctorAdminListItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DoctorAdminListItemDto>> CreateDoctor(
        [FromBody] CreateDoctorRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _doctorAdminService.CreateDoctorAsync(request, ct);
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

    /// <summary>PUT /api/v1/admin/doctors/{id} — Cập nhật thông tin chuyên khoa, khoa phòng, học vị, hồ sơ hành nghề và trạng thái hoạt động của bác sĩ.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType<DoctorAdminListItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DoctorAdminListItemDto>> UpdateDoctor(
        [FromRoute] string id,
        [FromBody] UpdateDoctorAdminRequest request,
        CancellationToken ct)
    {
        var result = await _doctorAdminService.UpdateDoctorAsync(
            GetUserId(),
            id,
            request,
            GetIp(),
            ct);
        return Ok(result);
    }

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId))
        {
            throw new UnauthorizedAccessException("Token không hợp lệ");
        }

        return userId;
    }

    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();
}

