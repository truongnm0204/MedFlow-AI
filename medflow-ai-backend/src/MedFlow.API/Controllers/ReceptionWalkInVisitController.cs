using System.Security.Claims;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class ReceptionWalkInVisitController : ControllerBase
{
    private readonly ICreateWalkInVisitService _walkInVisitService;

    public ReceptionWalkInVisitController(ICreateWalkInVisitService walkInVisitService)
    {
        _walkInVisitService = walkInVisitService;
    }

    /// <summary>
    /// POST /api/v1/reception/visits/walk-in — Tạo lượt khám ngoại trú vãng lai (Walk-in Visit) cho bệnh nhân không có lịch hẹn trước (UC25 / #76 / M2-024).
    /// </summary>
    [HttpPost("api/v1/reception/visits/walk-in")]
    [HttpPost("api/v1/reception/walk-in")]
    [HttpPost("api/v1/reception/walk-in/visits")]
    [HttpPost("api/v1/visits/walk-in")]
    [HttpPost("api/v1/reception/visits")]
    [ProducesResponseType<CreateWalkInVisitResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateWalkInVisitResponseDto>> CreateWalkInVisit(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateWalkInVisitRequest? request = null,
        CancellationToken ct = default)
    {
        var result = await _walkInVisitService.CreateWalkInVisitAsync(
            GetUserId(),
            request ?? new CreateWalkInVisitRequest(),
            routePatientIdOrCode: null,
            GetIp(),
            ct);

        return Created($"/api/v1/reception/visits/{result.VisitId}", result);
    }

    /// <summary>
    /// POST /api/v1/reception/patients/{patientIdOrCode}/walk-in — Tạo lượt khám ngoại trú vãng lai theo ID hoặc mã bệnh nhân trên đường dẫn (UC25 / #76 / M2-024).
    /// </summary>
    [HttpPost("api/v1/reception/patients/{patientIdOrCode}/walk-in")]
    [HttpPost("api/v1/reception/patients/{patientIdOrCode}/visits/walk-in")]
    [HttpPost("api/v1/patients/{patientIdOrCode}/visits/walk-in")]
    [ProducesResponseType<CreateWalkInVisitResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateWalkInVisitResponseDto>> CreateWalkInVisitForPatient(
        [FromRoute] string patientIdOrCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateWalkInVisitRequest? request = null,
        CancellationToken ct = default)
    {
        var result = await _walkInVisitService.CreateWalkInVisitAsync(
            GetUserId(),
            request ?? new CreateWalkInVisitRequest(),
            routePatientIdOrCode: patientIdOrCode,
            GetIp(),
            ct);

        return Created($"/api/v1/reception/visits/{result.VisitId}", result);
    }

    /// <summary>
    /// GET /api/v1/reception/walk-in/catalog — Lấy danh mục khoa phòng, dịch vụ khám ngoại trú và triệu chứng gợi ý tại cơ sở y tế (UC25 / #76).
    /// </summary>
    [HttpGet("api/v1/reception/walk-in/catalog")]
    [HttpGet("api/v1/reception/visits/walk-in/catalog")]
    [HttpGet("api/v1/reception/walk-in/departments")]
    [ProducesResponseType<WalkInCatalogResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalkInCatalogResponseDto>> GetWalkInCatalog(CancellationToken ct = default)
    {
        var result = await _walkInVisitService.GetWalkInCatalogAsync(GetUserId(), ct);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/v1/reception/visits/{visitIdOrCode}/walk-in — Xem thông tin lượt khám vãng lai theo ID hoặc mã lượt khám (UC25 / #76).
    /// </summary>
    [HttpGet("api/v1/reception/visits/{visitIdOrCode}/walk-in")]
    [HttpGet("api/v1/visits/{visitIdOrCode}/walk-in")]
    [HttpGet("api/v1/reception/walk-in/visits/{visitIdOrCode}")]
    [ProducesResponseType<CreateWalkInVisitResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateWalkInVisitResponseDto>> GetWalkInVisitByIdOrCode(
        [FromRoute] string visitIdOrCode,
        CancellationToken ct = default)
    {
        var result = await _walkInVisitService.GetWalkInVisitByIdOrCodeAsync(GetUserId(), visitIdOrCode, ct);
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
