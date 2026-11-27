using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

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
    // Optional logger: when supplied, publish failures are no longer silent. The publish remains
    // best-effort (never throws, never alters/fails a committed business transaction) — the logger
    // only adds an observable trace for diagnosis (E2E01 / PR #859 investigation).
    public static async Task TryPublishQueueChangedSafeAsync(
        this IRealtimeNotificationPublisher publisher,
        QueueChangedNotification notification,
        CancellationToken ct = default,
        ILogger? logger = null)
    {
        try
        {
            await publisher.PublishQueueChangedAsync(notification, ct);
        }
        catch (Exception ex)
        {
            // Post-commit best-effort notification: never alter or fail a committed business transaction.
            logger?.LogWarning(
                ex,
                "Realtime QueueChanged publish failed (hospitalId={HospitalId}, servicePointId={ServicePointId}).",
                notification?.HospitalId,
                notification?.ServicePointId);
        }
    }

    public static async Task TryPublishVisitPaymentChangedSafeAsync(
        this IRealtimeNotificationPublisher publisher,
        VisitPaymentChangedNotification notification,
        CancellationToken ct = default,
        ILogger? logger = null)
    {
        try
        {
            await publisher.PublishVisitPaymentChangedAsync(notification, ct);
        }
        catch (Exception ex)
        {
            // Post-commit best-effort notification: never fail a committed payment confirmation.
            logger?.LogWarning(
                ex,
                "Realtime VisitPaymentChanged publish failed (visitId={VisitId}).",
                notification?.VisitId);
        }
    }
}
