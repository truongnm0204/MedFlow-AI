using System.Security.Claims;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class VisitRoomTransferController : ControllerBase
{
    private readonly IVisitRoomTransferService _transferService;

    public VisitRoomTransferController(IVisitRoomTransferService transferService)
    {
        _transferService = transferService;
    }

    /// <summary>GET /api/v1/visits/{idOrCode}/compatible-rooms — Lấy danh sách phòng khám lâm sàng tương thích, sức chứa khả dụng và lịch sử điều chuyển phòng của lượt khám.</summary>
    [HttpGet("/api/v1/visits/{idOrCode}/compatible-rooms")]
    [HttpGet("/api/v1/reception/visits/{idOrCode}/compatible-rooms")]
    [HttpGet("/api/v1/visits/{idOrCode}/transfer-room")]
    [HttpGet("/api/v1/visits/{idOrCode}/room-transfers")]
    [ProducesResponseType<VisitCompatibleRoomsResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisitCompatibleRoomsResponseDto>> GetCompatibleRooms(
        [FromRoute] string idOrCode,
        CancellationToken ct)
    {
        var result = await _transferService.GetCompatibleRoomsAsync(GetUserId(), idOrCode, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/visits/{idOrCode}/transfer-room — Điều chuyển bệnh nhân sang phòng khám lâm sàng tương thích khác.</summary>
    [HttpPost("/api/v1/visits/{idOrCode}/transfer-room")]
    [HttpPost("/api/v1/reception/visits/{idOrCode}/transfer-room")]
    [HttpPut("/api/v1/visits/{idOrCode}/transfer-room")]
    [HttpPatch("/api/v1/visits/{idOrCode}/examination-room/transfer")]
    [ProducesResponseType<VisitRoomTransferResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitRoomTransferResponseDto>> TransferRoom(
        [FromRoute] string idOrCode,
        [FromBody] TransferVisitRoomRequest request,
        CancellationToken ct)
    {
        var result = await _transferService.TransferToCompatibleRoomAsync(
            GetUserId(),
            idOrCode,
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
