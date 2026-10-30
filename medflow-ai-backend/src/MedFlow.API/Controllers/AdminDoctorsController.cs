using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Services.Doctors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/doctors")]
[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public class AdminDoctorsController : ControllerBase
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
}

