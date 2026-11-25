using MedFlow.Application.DTOs.Notifications;

namespace MedFlow.Application.Services.Notifications;

public interface INotificationService
{
    Task<NotificationFeedResponse> GetMyNotificationsAsync(
        long userId,
        GetNotificationsRequest? request,
        CancellationToken ct = default);

    Task<MarkNotificationReadResponse> MarkAsReadAsync(
        long userId,
        long notificationId,
        CancellationToken ct = default);

    Task<MarkAllNotificationsReadResponse> MarkAllAsReadAsync(
        long userId,
        CancellationToken ct = default);
}
