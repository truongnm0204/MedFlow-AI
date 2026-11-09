using System.Security.Claims;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Services.Policies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/admin/queue-policy")]
[Authorize(Roles = "HOSPITAL_ADMIN")]
public sealed class QueuePolicyController : ControllerBase
{
    private readonly IQueuePolicyService _service;

    public QueuePolicyController(IQueuePolicyService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/admin/queue-policy — Lấy cấu hình chính sách điều phối hàng đợi của bệnh viện quản trị.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(QueuePolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<QueuePolicyDto>> Get(CancellationToken ct)
        => Ok(await _service.GetForAdminAsync(GetUserId(), ct));

    /// <summary>PUT /api/v1/admin/queue-policy — Cập nhật cấu hình chính sách điều phối hàng đợi của bệnh viện quản trị.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(QueuePolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<QueuePolicyDto>> Update(
        [FromBody] UpdateQueuePolicyRequest request,
        CancellationToken ct)
        => Ok(await _service.UpdateForAdminAsync(GetUserId(), request, GetIp(), ct));

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("Token không hợp lệ");
        return long.Parse(sub);
    }

    private string? GetIp()
        => Request.Headers.TryGetValue("X-Forwarded-For", out var ff)
            ? ff.ToString().Split(',')[0].Trim()
            : HttpContext.Connection.RemoteIpAddress?.ToString();
}
