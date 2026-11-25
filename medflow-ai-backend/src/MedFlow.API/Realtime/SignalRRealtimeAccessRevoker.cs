using MedFlow.API.Hubs;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace MedFlow.API.Realtime;

public sealed class SignalRRealtimeAccessRevoker : IRealtimeAccessRevoker
{
    private readonly IHubContext<ClinicalRealtimeHub> _hubContext;
    private readonly ClinicalRealtimeConnectionRegistry _registry;
    private readonly IOptionsMonitor<RealtimeOptions> _optionsMonitor;
    private readonly ILogger<SignalRRealtimeAccessRevoker> _logger;

    public SignalRRealtimeAccessRevoker(
        IHubContext<ClinicalRealtimeHub> hubContext,
        ClinicalRealtimeConnectionRegistry registry,
        IOptionsMonitor<RealtimeOptions> optionsMonitor,
        ILogger<SignalRRealtimeAccessRevoker> logger)
    {
        _hubContext = hubContext;
        _registry = registry;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task RevokeFamilyMemberAccessAsync(
        long ownerUserId,
        long memberPatientId,
        CancellationToken ct = default)
    {
        var evicted = _registry.EvictFamilyMemberSubscriptions(ownerUserId, memberPatientId);
        await ApplyEvictionsAsync(evicted, "FAMILY_LINK_REVOKED");
    }

    public async Task RevokeAppointmentTrackingAsync(
        long appointmentId,
        CancellationToken ct = default)
    {
        var evicted = _registry.EvictAppointmentSubscriptions(appointmentId);
        await ApplyEvictionsAsync(evicted, "APPOINTMENT_INACTIVE");
    }

    public async Task MigrateAppointmentBucketAsync(
        long appointmentId,
        long? newServicePointId,
        long? newRoomId,
        CancellationToken ct = default)
    {
        var newBuckets = new List<string>(2);
        if (newServicePointId is > 0)
            newBuckets.Add(RealtimeGroupNames.PatientBucketServicePoint(newServicePointId.Value));
        if (newRoomId is > 0)
            newBuckets.Add(RealtimeGroupNames.PatientBucketRoom(newRoomId.Value));

        var migrations = _registry.MigrateAppointmentBuckets(appointmentId, newBuckets);
        if (migrations.Count == 0)
            return;

        using var cts = CreateBoundedTimeoutTokenSource();
        var timeoutToken = cts.Token;

        try
        {
            foreach (var item in migrations)
            {
                foreach (var oldGroup in item.GroupsToRemove)
                    await _hubContext.Groups.RemoveFromGroupAsync(item.ConnectionId, oldGroup, timeoutToken);

                foreach (var newGroup in item.GroupsToAdd)
                    await _hubContext.Groups.AddToGroupAsync(item.ConnectionId, newGroup, timeoutToken);

                await _hubContext.Clients
                    .Client(item.ConnectionId)
                    .SendAsync(
                        RealtimeEventNames.QueueScopeChanged,
                        new QueueScopeChangedWireDto(item.AppointmentId),
                        timeoutToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to complete SignalR bucket migration for appointment {AppointmentId}.",
                appointmentId);
        }
    }

    public async Task RevokeUserConnectionsAsync(
        long userId,
        string reasonCode = "ACCOUNT_REVOKED",
        CancellationToken ct = default)
    {
        var evicted = _registry.EvictUserSubscriptions(userId);
        await ApplyEvictionsAsync(evicted, reasonCode);
    }

    public async Task RevokeUserConnectionsExceptSessionAsync(
        long userId,
        long? keepSessionId,
        string reasonCode = "SESSION_REVOKED",
        CancellationToken ct = default)
    {
        var evicted = _registry.EvictUserSubscriptionsExceptSession(userId, keepSessionId);
        await ApplyEvictionsAsync(evicted, reasonCode);
    }

    public async Task RevokeUserConnectionsExceptSessionFamilyAsync(
        long userId,
        IReadOnlyCollection<long>? keepSessionIds,
        string reasonCode = "SESSION_REVOKED",
        CancellationToken ct = default)
    {
        var evicted = _registry.EvictUserSubscriptionsExceptSessionFamily(userId, keepSessionIds);
        await ApplyEvictionsAsync(evicted, reasonCode);
    }

    public async Task RevokeUserSessionFamilyConnectionsAsync(
        long userId,
        IReadOnlyCollection<long> sessionIds,
        string reasonCode = "LOGOUT",
        CancellationToken ct = default)
    {
        var evicted = _registry.EvictUserSessionFamilySubscriptions(userId, sessionIds);
        await ApplyEvictionsAsync(evicted, reasonCode);
    }

    private async Task ApplyEvictionsAsync(
        IReadOnlyList<EvictedSubscriptionItem> evicted,
        string reasonCode)
    {
        if (evicted.Count == 0)
            return;

        using var cts = CreateBoundedTimeoutTokenSource();
        var timeoutToken = cts.Token;

        try
        {
            foreach (var item in evicted)
            {
                foreach (var group in item.GroupsToRemove)
                    await _hubContext.Groups.RemoveFromGroupAsync(item.ConnectionId, group, timeoutToken);

                await _hubContext.Clients
                    .Client(item.ConnectionId)
                    .SendAsync(
                        RealtimeEventNames.MembershipRevoked,
                        new MembershipRevokedWireDto(item.ResourceKey, reasonCode),
                        timeoutToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to apply SignalR membership eviction ({ReasonCode}, count={Count}).",
                reasonCode,
                evicted.Count);
        }
    }

    private CancellationTokenSource CreateBoundedTimeoutTokenSource()
    {
        var seconds = Math.Clamp(_optionsMonitor.CurrentValue.PublishTimeoutSeconds, 1, 15);
        return new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    }
}
