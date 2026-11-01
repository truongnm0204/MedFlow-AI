using MedFlow.Application.DTOs.ExaminationRooms;
using MedFlow.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/examination-rooms")]
[Route("api/v1/admin/rooms")]
[Route("api/v1/examination-rooms")]
[Authorize(Roles = "HOSPITAL_ADMIN,SYSTEM_ADMIN")]
public sealed class AdminExaminationRoomsController : ControllerBase
{
    private readonly IExaminationRoomService _examinationRoomService;

    public AdminExaminationRoomsController(IExaminationRoomService examinationRoomService)
    {
        _examinationRoomService = examinationRoomService;
    }

    /// <summary>GET /api/v1/admin/examination-rooms — Lấy danh sách buồng khám và trạng thái khả dụng.</summary>
    [HttpGet]
    [ProducesResponseType<ExaminationRoomListResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ExaminationRoomListResponseDto>> GetExaminationRoomList(
        [FromQuery] GetExaminationRoomListRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _examinationRoomService.GetExaminationRoomListAsync(request, ct);
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
