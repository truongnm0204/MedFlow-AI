using System.Security.Claims;
using MedFlow.Application.DTOs.Notifications;
using MedFlow.Application.Services.Notifications;
using MedFlow.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.API.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize(Roles = RoleNames.Patient)]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>GET /api/v1/notifications — Xem danh sách thông báo, số chưa đọc và lịch nhắc sắp tới của bệnh nhân đăng nhập (UC17).</summary>
    [HttpGet]
    [ProducesResponseType<NotificationFeedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<NotificationFeedResponse>> GetMyNotifications(
        [FromQuery] GetNotificationsRequest request,
        CancellationToken ct)
        => Ok(await _notificationService.GetMyNotificationsAsync(GetUserId(), request, ct));

    /// <summary>POST|PATCH /api/v1/notifications/{id}/read — Đánh dấu một thông báo thuộc phạm vi sở hữu là đã đọc.</summary>
    [HttpPost("{id:long}/read")]
    [HttpPatch("{id:long}/read")]
    [ProducesResponseType<MarkNotificationReadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MarkNotificationReadResponse>> MarkAsRead(
        long id,
        CancellationToken ct)
        => Ok(await _notificationService.MarkAsReadAsync(GetUserId(), id, ct));

    /// <summary>POST /api/v1/notifications/read-all — Đánh dấu toàn bộ thông báo chưa đọc của bệnh nhân là đã đọc.</summary>
    [HttpPost("read-all")]
    [ProducesResponseType<MarkAllNotificationsReadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MarkAllNotificationsReadResponse>> MarkAllAsRead(
        CancellationToken ct)
        => Ok(await _notificationService.MarkAllAsReadAsync(GetUserId(), ct));

    private long GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !long.TryParse(sub, out var userId) || userId <= 0)
        {
            throw new UnauthorizedAccessException("Token không hợp lệ");
        }

        return userId;
    }
}
