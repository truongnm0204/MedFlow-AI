using System.Security.Claims;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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

    /// <summary>POST /api/v1/admin/examination-services — Tạo mới dịch vụ khám.</summary>
    [HttpPost]
    [ProducesResponseType<ExaminationServiceAdminItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExaminationServiceAdminItemDto>> CreateExaminationService(
        [FromBody] CreateExaminationServiceRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.CreateExaminationServiceAsync(request, ct);
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

    /// <summary>PUT /api/v1/admin/examination-services/{id} — Cập nhật thông tin dịch vụ khám.</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<ExaminationServiceAdminItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExaminationServiceAdminItemDto>> UpdateExaminationService(
        [FromRoute] long id,
        [FromBody] UpdateExaminationServiceRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.UpdateExaminationServiceAsync(id, request, ct);
            return Ok(result);
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

    /// <summary>POST|PATCH|PUT /api/v1/admin/examination-services/{idOrCode}/deactivate hoặc DELETE {idOrCode} — Ngừng hoạt động dịch vụ khám (UC50 / #196).</summary>
    [HttpPost("{idOrCode}/deactivate")]
    [HttpPatch("{idOrCode}/deactivate")]
    [HttpPut("{idOrCode}/deactivate")]
    [HttpDelete("{idOrCode}")]
    [ProducesResponseType<DeactivateExaminationServiceResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeactivateExaminationServiceResponseDto>> DeactivateExaminationService(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeactivateExaminationServiceRequest? request = null,
        CancellationToken ct = default)
    {
        var result = await _service.DeactivateExaminationServiceAsync(
            GetUserId(),
            idOrCode,
            request ?? new DeactivateExaminationServiceRequest(),
            GetIp(),
            ct);
        return Ok(result);
    }

    /// <summary>POST|PATCH|PUT /api/v1/admin/examination-services/{idOrCode}/reactivate — Kích hoạt lại dịch vụ khám đã ngừng hoạt động (UC50 / #196).</summary>
    [HttpPost("{idOrCode}/reactivate")]
    [HttpPatch("{idOrCode}/reactivate")]
    [HttpPut("{idOrCode}/reactivate")]
    [ProducesResponseType<DeactivateExaminationServiceResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeactivateExaminationServiceResponseDto>> ReactivateExaminationService(
        [FromRoute] string idOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReactivateExaminationServiceRequest? request = null,
        CancellationToken ct = default)
    {
        var result = await _service.ReactivateExaminationServiceAsync(
            GetUserId(),
            idOrCode,
            request,
            GetIp(),
            ct);
        return Ok(result);
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

    private string? GetIp() =>
        HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString();
}

