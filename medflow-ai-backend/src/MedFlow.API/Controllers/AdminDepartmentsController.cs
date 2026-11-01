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
}

