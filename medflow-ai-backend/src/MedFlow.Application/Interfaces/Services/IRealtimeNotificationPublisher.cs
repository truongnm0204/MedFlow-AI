using MedFlow.Application.DTOs.Realtime;

namespace MedFlow.Application.Interfaces.Services;

public interface IRealtimeNotificationPublisher
{
    Task PublishQueueChangedAsync(
        QueueChangedNotification notification,
        CancellationToken ct = default);

    Task PublishVisitPaymentChangedAsync(
        VisitPaymentChangedNotification notification,
        CancellationToken ct = default);
}
