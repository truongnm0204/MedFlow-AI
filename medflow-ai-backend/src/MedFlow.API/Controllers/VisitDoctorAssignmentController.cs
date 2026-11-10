using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/visits")]
[Route("api/v1/reception/visits")]
[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class VisitDoctorAssignmentController : ControllerBase
{
    private readonly IVisitDoctorAssignmentService _visitDoctorAssignmentService;

    public VisitDoctorAssignmentController(IVisitDoctorAssignmentService visitDoctorAssignmentService)
    {
        _visitDoctorAssignmentService = visitDoctorAssignmentService;
    }

    /// <summary>GET /api/v1/visits/{visitIdOrCode}/available-doctors — Lấy danh sách bác sĩ phù hợp kèm khả dụng và tải lượng hàng đợi cho lượt khám (UC26).</summary>
    [HttpGet("{visitIdOrCode}/available-doctors")]
    [HttpGet("{visitIdOrCode}/assignable-doctors")]
    [HttpGet("{visitIdOrCode}/doctors")]
    [ProducesResponseType<AssignableDoctorListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssignableDoctorListResponseDto>> GetAssignableDoctors(
        [FromRoute] string visitIdOrCode,
        [FromQuery] long? serviceId = null,
        [FromQuery] string? serviceCode = null,
        [FromQuery] long? departmentId = null,
        CancellationToken ct = default)
    {
        var result = await _visitDoctorAssignmentService.GetAssignableDoctorsAsync(
            GetUserId(),
            visitIdOrCode,
            serviceId,
            serviceCode,
            departmentId,
            ct);
        return Ok(result);
    }

    /// <summary>PUT /api/v1/visits/{visitIdOrCode}/doctor — Phân công bác sĩ phụ trách cho lượt khám ngoại trú (UC26).</summary>
    [HttpPut("{visitIdOrCode}/doctor")]
    [HttpPost("{visitIdOrCode}/doctor")]
    [HttpPatch("{visitIdOrCode}/doctor")]
    [HttpPut("{visitIdOrCode}/assign-doctor")]
    [HttpPost("{visitIdOrCode}/assign-doctor")]
    [ProducesResponseType<AssignDoctorToVisitResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssignDoctorToVisitResponseDto>> AssignDoctor(
        [FromRoute] string visitIdOrCode,
        [FromBody] AssignDoctorToVisitRequest request,
        CancellationToken ct)
    {
        var result = await _visitDoctorAssignmentService.AssignDoctorAsync(
            GetUserId(),
            visitIdOrCode,
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
