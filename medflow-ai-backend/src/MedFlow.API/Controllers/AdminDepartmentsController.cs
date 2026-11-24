using System.Security.Claims;
using MedFlow.Application.DTOs.Departments;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/departments")]
[Route("api/v1/departments")]
[Authorize(Roles = $"{RoleNames.HospitalAdmin},{RoleNames.SystemAdmin}")]
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
            var result = await _departmentService.GetDepartmentListAsync(RequireUserId(), request, ct);
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
            var result = await _departmentService.CreateDepartmentAsync(RequireUserId(), request, ct);
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

    /// <summary>PUT /api/v1/admin/departments/{id} — Cập nhật thông tin khoa phòng.</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<DepartmentItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentItemDto>> UpdateDepartment(
        [FromRoute] long id,
        [FromBody] UpdateDepartmentRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _departmentService.UpdateDepartmentAsync(RequireUserId(), id, request, ct);
            return Ok(result);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy khoa phòng",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
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
                Title = "Xung đột dữ liệu",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy khoa phòng",
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

    /// <summary>GET /api/v1/admin/departments/{idOrCode}/dependencies — Kiểm tra các ràng buộc phụ thuộc của khoa phòng.</summary>
    [HttpGet("{idOrCode}/dependencies")]
    [ProducesResponseType<DepartmentDependencySummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentDependencySummaryDto>> CheckDependencies(
        [FromRoute] string idOrCode,
        CancellationToken ct)
    {
        var result = await _departmentService.CheckDepartmentDependenciesAsync(RequireUserId(), idOrCode, ct);
        return Ok(result);
    }

    /// <summary>POST|PATCH|PUT /api/v1/admin/departments/{idOrCode}/deactivate (cùng alias DELETE /api/v1/admin/departments/{idOrCode}) — Ngừng hoạt động khoa phòng (UC47 / #172).</summary>
    [HttpPost("{idOrCode}/deactivate")]
    [HttpPatch("{idOrCode}/deactivate")]
    [HttpPut("{idOrCode}/deactivate")]
    [HttpDelete("{idOrCode}")]
    [ProducesResponseType<DeactivateDepartmentResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeactivateDepartmentResponseDto>> DeactivateDepartment(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeactivateDepartmentRequest? request = null,
        CancellationToken ct = default)
    {
        var result = await _departmentService.DeactivateDepartmentAsync(
            RequireUserId(),
            idOrCode,
            request ?? new DeactivateDepartmentRequest(),
            GetIp(),
            ct);
        return Ok(result);
    }

    /// <summary>POST|PATCH|PUT /api/v1/admin/departments/{idOrCode}/reactivate — Kích hoạt lại khoa phòng đã ngừng hoạt động (UC47 / #172).</summary>
    [HttpPost("{idOrCode}/reactivate")]
    [HttpPatch("{idOrCode}/reactivate")]
    [HttpPut("{idOrCode}/reactivate")]
    [ProducesResponseType<DeactivateDepartmentResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeactivateDepartmentResponseDto>> ReactivateDepartment(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReactivateDepartmentRequest? request = null,
        CancellationToken ct = default)
    {
        var result = await _departmentService.ReactivateDepartmentAsync(
            RequireUserId(),
            idOrCode,
            request,
            GetIp(),
            ct);
        return Ok(result);
    }

    private long RequireUserId()
    {
        var userId = GetUserId();
        if (!userId.HasValue || userId.Value <= 0)
        {
            throw new UnauthorizedAccessException("Token không hợp lệ.");
        }

        return userId.Value;
    }

    private long? GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId))
        {
            return null;
        }

        return userId;
    }

    private string? GetIp()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) &&
            !string.IsNullOrWhiteSpace(forwardedFor))
        {
            return forwardedFor.ToString().Split(',')[0].Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
