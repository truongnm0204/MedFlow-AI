using MedFlow.Application.DTOs.Departments;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/departments")]
[Route("api/v1/departments")]
[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class AdminDepartmentsController : ControllerBase
{
    private readonly IDepartmentService _departmentService;

    public AdminDepartmentsController(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    /// <summary>GET /api/v1/admin/departments — Lấy danh sách khoa phòng với tìm kiếm, lọc theo trạng thái và phân trang.</summary>
    [HttpGet]
    [ProducesResponseType<DepartmentListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DepartmentListResponseDto>> GetDepartmentList(
        [FromQuery] GetDepartmentListRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _departmentService.GetDepartmentListAsync(request, ct);
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

    /// <summary>POST /api/v1/admin/departments — Tạo mới khoa phòng.</summary>
    [HttpPost]
    [ProducesResponseType<DepartmentItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DepartmentItemDto>> CreateDepartment(
        [FromBody] CreateDepartmentRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _departmentService.CreateDepartmentAsync(request, ct);
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
}
