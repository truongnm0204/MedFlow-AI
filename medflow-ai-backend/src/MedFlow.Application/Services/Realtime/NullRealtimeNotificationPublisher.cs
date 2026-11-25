using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Services;

namespace MedFlow.Application.Services.Realtime;

public sealed class NullRealtimeNotificationPublisher : IRealtimeNotificationPublisher
{
    public static readonly NullRealtimeNotificationPublisher Instance = new();

    public Task PublishQueueChangedAsync(
        QueueChangedNotification notification,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task PublishVisitPaymentChangedAsync(
        VisitPaymentChangedNotification notification,
        CancellationToken ct = default) => Task.CompletedTask;
}

public static class RealtimePublishSafeExtensions
{
    public static async Task TryPublishQueueChangedSafeAsync(
        this IRealtimeNotificationPublisher publisher,
        QueueChangedNotification notification,
        CancellationToken ct = default)
    {
        try
        {
            await publisher.PublishQueueChangedAsync(notification, ct);
        }
        catch
        {
            // Post-commit best-effort notification: publisher logs sanitized details;
            // never alter or fail a committed business transaction.
        }
    }

    public static async Task TryPublishVisitPaymentChangedSafeAsync(
        this IRealtimeNotificationPublisher publisher,
        VisitPaymentChangedNotification notification,
        CancellationToken ct = default)
    {
        try
        {
            await publisher.PublishVisitPaymentChangedAsync(notification, ct);
        }
        catch
        {
            // Post-commit best-effort notification: never fail a committed payment confirmation.
        }
    }
}
