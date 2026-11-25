using MedFlow.API.Hubs;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace MedFlow.API.Realtime;

public sealed class SignalRRealtimeNotificationPublisher : IRealtimeNotificationPublisher
{
    private readonly IHubContext<ClinicalRealtimeHub> _hubContext;
    private readonly ClinicalRealtimeConnectionRegistry _registry;
    private readonly IOptionsMonitor<RealtimeOptions> _optionsMonitor;
    private readonly ILogger<SignalRRealtimeNotificationPublisher> _logger;

    public SignalRRealtimeNotificationPublisher(
        IHubContext<ClinicalRealtimeHub> hubContext,
        ClinicalRealtimeConnectionRegistry registry,
        IOptionsMonitor<RealtimeOptions> optionsMonitor,
        ILogger<SignalRRealtimeNotificationPublisher> logger)
    {
        _hubContext = hubContext;
        _registry = registry;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task PublishQueueChangedAsync(
        QueueChangedNotification notification,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        using var cts = CreateBoundedTimeoutTokenSource();
        var publishToken = cts.Token;

        try
        {
            await PruneExpiredGroupsAsync(publishToken);

            var targetGroups = new HashSet<string>(StringComparer.Ordinal);
            if (notification.HospitalId > 0)
            {
                targetGroups.Add(RealtimeGroupNames.HospitalQueue(notification.HospitalId));

                if (notification.ServicePointId is > 0)
                {
                    targetGroups.Add(
                        RealtimeGroupNames.HospitalServicePoint(
                            notification.HospitalId,
                            notification.ServicePointId.Value));
                }

                if (notification.RoomId is > 0)
                {
                    targetGroups.Add(
                        RealtimeGroupNames.HospitalRoom(
                            notification.HospitalId,
                            notification.RoomId.Value));
                }
            }

            if (notification.AppointmentId is > 0)
                targetGroups.Add(RealtimeGroupNames.AppointmentQueue(notification.AppointmentId.Value));

            if (notification.ServicePointId is > 0)
                targetGroups.Add(RealtimeGroupNames.PatientBucketServicePoint(notification.ServicePointId.Value));

            if (notification.RoomId is > 0)
                targetGroups.Add(RealtimeGroupNames.PatientBucketRoom(notification.RoomId.Value));

            if (targetGroups.Count == 0)
                return;

            var wirePayload = new QueueChangedWireDto(
                EventType: RealtimeEventNames.QueueChanged,
                ServicePointId: notification.ServicePointId);

            await _hubContext.Clients
                .Groups(targetGroups.ToArray())
                .SendAsync(RealtimeEventNames.QueueChanged, wirePayload, publishToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SignalR QueueChanged publish failed (hospitalId={HospitalId}, servicePointId={ServicePointId}).",
                notification.HospitalId,
                notification.ServicePointId);
        }
    }

    public async Task PublishVisitPaymentChangedAsync(
        VisitPaymentChangedNotification notification,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.VisitId <= 0)
            return;

        using var cts = CreateBoundedTimeoutTokenSource();
        var publishToken = cts.Token;

        try
        {
            await PruneExpiredGroupsAsync(publishToken);

            var groupName = RealtimeGroupNames.VisitPayment(notification.VisitId);
            var wirePayload = new VisitPaymentChangedWireDto(notification.VisitId);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync(RealtimeEventNames.VisitPaymentChanged, wirePayload, publishToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SignalR VisitPaymentChanged publish failed (visitId={VisitId}).",
                notification.VisitId);
        }
    }

    private async Task PruneExpiredGroupsAsync(CancellationToken ct)
    {
        var expired = _registry.PruneAllExpiredSubscriptions();
        if (expired.Count == 0)
            return;

        foreach (var item in expired)
        {
            foreach (var group in item.GroupsToRemove)
                await _hubContext.Groups.RemoveFromGroupAsync(item.ConnectionId, group, ct);

            await _hubContext.Clients
                .Client(item.ConnectionId)
                .SendAsync(
                    RealtimeEventNames.MembershipRevoked,
                    new MembershipRevokedWireDto(item.ResourceKey, "LEASE_EXPIRED"),
                    ct);
        }
    }

    private CancellationTokenSource CreateBoundedTimeoutTokenSource()
    {
        var seconds = Math.Clamp(_optionsMonitor.CurrentValue.PublishTimeoutSeconds, 1, 15);
        return new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    }
}
