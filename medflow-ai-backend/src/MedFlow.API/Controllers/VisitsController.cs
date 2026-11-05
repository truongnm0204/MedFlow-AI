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
public sealed class VisitsController : ControllerBase
{
    private readonly IVisitRoomAssignmentService _visitRoomAssignmentService;

    public VisitsController(IVisitRoomAssignmentService visitRoomAssignmentService)
    {
        _visitRoomAssignmentService = visitRoomAssignmentService;
    }

    /// <summary>GET /api/v1/visits/{visitIdOrCode}/available-rooms — Lấy danh sách buồng khám tương thích và khả dụng cho lượt khám.</summary>
    [HttpGet("{visitIdOrCode}/available-rooms")]
    [HttpGet("{visitIdOrCode}/assignable-rooms")]
    [ProducesResponseType<CompatibleExaminationRoomListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompatibleExaminationRoomListResponseDto>> GetCompatibleRooms(
        [FromRoute] string visitIdOrCode,
        CancellationToken ct)
    {
        var result = await _visitRoomAssignmentService.GetCompatibleRoomsAsync(
            GetUserId(),
            visitIdOrCode,
            ct);
        return Ok(result);
    }

    /// <summary>PUT /api/v1/visits/{visitIdOrCode}/examination-room — Gán buồng khám lâm sàng tương thích cho lượt khám ngoại trú (UC27).</summary>
    [HttpPut("{visitIdOrCode}/examination-room")]
    [HttpPost("{visitIdOrCode}/examination-room")]
    [HttpPut("{visitIdOrCode}/assign-room")]
    [HttpPost("{visitIdOrCode}/assign-room")]
    [ProducesResponseType<AssignExaminationRoomResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssignExaminationRoomResponseDto>> AssignExaminationRoom(
        [FromRoute] string visitIdOrCode,
        [FromBody] AssignExaminationRoomRequest request,
        CancellationToken ct)
    {
        var result = await _visitRoomAssignmentService.AssignExaminationRoomAsync(
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
