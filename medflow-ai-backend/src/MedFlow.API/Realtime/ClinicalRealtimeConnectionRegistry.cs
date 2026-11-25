using System.Collections.Concurrent;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Options;
using Microsoft.Extensions.Options;

namespace MedFlow.API.Realtime;

public sealed record TrackedSubscriptionEntry(
    string ConnectionId,
    long UserId,
    string ResourceKey,
    string? AliasKey,
    IReadOnlyList<string> PrimaryGroupNames,
    IReadOnlyList<string> BucketGroupNames,
    long? HospitalId,
    long? PatientId,
    long? AppointmentId,
    long? VisitId,
    DateTime ExpiresAtUtc)
{
    public IReadOnlyList<string> AllGroupNames =>
        BucketGroupNames.Count == 0
            ? PrimaryGroupNames
            : PrimaryGroupNames.Concat(BucketGroupNames).Distinct(StringComparer.Ordinal).ToArray();
}

public sealed record UpsertSubscriptionOutcome(
    bool Allowed,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime ExpiresAtUtc,
    IReadOnlyList<string> GroupsToAdd,
    IReadOnlyList<string> GroupsToRemove,
    IReadOnlyList<string> PrunedExpiredGroups);

public sealed record LeaveSubscriptionOutcome(
    bool Allowed,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyList<string> GroupsToRemove,
    IReadOnlyList<string> PrunedExpiredGroups);

public sealed record EvictedSubscriptionItem(
    string ConnectionId,
    string ResourceKey,
    IReadOnlyList<string> GroupsToRemove);

public sealed record BucketMigrationItem(
    string ConnectionId,
    long AppointmentId,
    IReadOnlyList<string> GroupsToRemove,
    IReadOnlyList<string> GroupsToAdd);

public sealed class ClinicalRealtimeConnectionRegistry
{
    private sealed class ConnectionSessionState
    {
        public readonly object SyncRoot = new();
        public required long UserId { get; init; }
        public long? SessionId { get; init; }
        public bool IsRevoked { get; set; }
        public readonly Dictionary<string, TrackedSubscriptionEntry> Subscriptions = new(StringComparer.OrdinalIgnoreCase);
        public readonly Queue<DateTime> InvocationTimestampsUtc = new();
    }

    private readonly ConcurrentDictionary<string, ConnectionSessionState> _connections = new(StringComparer.Ordinal);
    private readonly IOptionsMonitor<RealtimeOptions> _optionsMonitor;
    private readonly TimeProvider _timeProvider;

    public ClinicalRealtimeConnectionRegistry(
        IOptionsMonitor<RealtimeOptions> optionsMonitor,
        TimeProvider? timeProvider = null)
    {
        _optionsMonitor = optionsMonitor;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void RegisterConnection(string connectionId, long userId, long? sessionId = null)
    {
        _connections[connectionId] = new ConnectionSessionState
        {
            UserId = userId,
            SessionId = sessionId,
        };
    }

    public void UnregisterConnection(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }

    public bool IsConnectionRevoked(string connectionId)
    {
        if (!_connections.TryGetValue(connectionId, out var session))
            return false;

        lock (session.SyncRoot)
        {
            return session.IsRevoked;
        }
    }

    public IReadOnlyList<string> EvictConnection(string connectionId)
    {
        if (!_connections.TryGetValue(connectionId, out var session))
            return [];

        lock (session.SyncRoot)
        {
            session.IsRevoked = true;
            if (session.Subscriptions.Count == 0)
                return [];

            var groups = session.Subscriptions.Values
                .SelectMany(s => s.AllGroupNames)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            session.Subscriptions.Clear();
            return groups;
        }
    }

    public bool TryCheckInvocationRateLimit(string connectionId, out string? errorMessage)
    {
        errorMessage = null;
        if (!_connections.TryGetValue(connectionId, out var session))
            return true;

        var opts = _optionsMonitor.CurrentValue;
        var maxRpm = Math.Max(1, opts.MaxInvocationsPerMinute);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var windowStart = nowUtc.AddMinutes(-1);

        lock (session.SyncRoot)
        {
            while (session.InvocationTimestampsUtc.Count > 0 && session.InvocationTimestampsUtc.Peek() <= windowStart)
                session.InvocationTimestampsUtc.Dequeue();

            if (session.InvocationTimestampsUtc.Count >= maxRpm)
            {
                errorMessage = $"[RATE_LIMITED] Quá giới hạn tần suất thao tác kênh thời gian thực ({maxRpm} lần/phút).";
                return false;
            }

            session.InvocationTimestampsUtc.Enqueue(nowUtc);
            return true;
        }
    }

    public UpsertSubscriptionOutcome TryUpsertSubscription(
        string connectionId,
        long userId,
        AuthorizedRealtimeSubscription grant,
        string? rawAliasKey = null)
    {
        var session = _connections.GetOrAdd(connectionId, _ => new ConnectionSessionState { UserId = userId });
        var opts = _optionsMonitor.CurrentValue;
        var maxGroups = Math.Max(1, opts.MaxGroupsPerConnection);
        var ttlMinutes = Math.Max(1, opts.MembershipTtlMinutes);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = nowUtc.AddMinutes(ttlMinutes);
        var normalizedAlias = NormalizeAlias(rawAliasKey);

        lock (session.SyncRoot)
        {
            if (session.IsRevoked)
            {
                return new UpsertSubscriptionOutcome(
                    Allowed: false,
                    ErrorCode: "UNAUTHORIZED",
                    ErrorMessage: "[UNAUTHORIZED] Phiên kết nối thời gian thực đã bị thu hồi.",
                    ExpiresAtUtc: nowUtc,
                    GroupsToAdd: [],
                    GroupsToRemove: [],
                    PrunedExpiredGroups: []);
            }

            var prunedExpiredGroups = PruneExpiredInsideLock(session, nowUtc);

            session.Subscriptions.TryGetValue(grant.ResourceKey, out var existing);

            var projectedGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kvp in session.Subscriptions)
            {
                if (string.Equals(kvp.Key, grant.ResourceKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var g in kvp.Value.AllGroupNames)
                    projectedGroups.Add(g);
            }

            var newEntryGroups = grant.AllGroupNames;
            var totalDistinctGroups = projectedGroups.Count + newEntryGroups.Count(g => !projectedGroups.Contains(g));
            if (totalDistinctGroups > maxGroups)
            {
                return new UpsertSubscriptionOutcome(
                    Allowed: false,
                    ErrorCode: "GROUP_LIMIT_EXCEEDED",
                    ErrorMessage: $"[GROUP_LIMIT_EXCEEDED] Vượt quá số lượng nhóm thời gian thực tối đa ({maxGroups}) trên một kết nối.",
                    ExpiresAtUtc: nowUtc,
                    GroupsToAdd: [],
                    GroupsToRemove: [],
                    PrunedExpiredGroups: prunedExpiredGroups);
            }

            var updatedEntry = new TrackedSubscriptionEntry(
                ConnectionId: connectionId,
                UserId: userId,
                ResourceKey: grant.ResourceKey,
                AliasKey: normalizedAlias,
                PrimaryGroupNames: grant.PrimaryGroupNames,
                BucketGroupNames: grant.BucketGroupNames,
                HospitalId: grant.HospitalId,
                PatientId: grant.PatientId,
                AppointmentId: grant.AppointmentId,
                VisitId: grant.VisitId,
                ExpiresAtUtc: expiresAtUtc);

            session.Subscriptions[grant.ResourceKey] = updatedEntry;

            var stillReferencedByOtherSubs = CollectActiveGroupsExcept(session, grant.ResourceKey);
            var groupsToRemove = existing is null
                ? []
                : existing.AllGroupNames
                    .Where(oldG =>
                        !newEntryGroups.Contains(oldG, StringComparer.Ordinal) &&
                        !stillReferencedByOtherSubs.Contains(oldG))
                    .ToArray();

            var groupsToAdd = newEntryGroups.ToArray();

            return new UpsertSubscriptionOutcome(
                Allowed: true,
                ErrorCode: null,
                ErrorMessage: null,
                ExpiresAtUtc: expiresAtUtc,
                GroupsToAdd: groupsToAdd,
                GroupsToRemove: groupsToRemove,
                PrunedExpiredGroups: prunedExpiredGroups);
        }
    }

    public LeaveSubscriptionOutcome LeaveByResourceOrAlias(
        string connectionId,
        string? resourceKeyPrefix,
        string? rawIdentifier)
    {
        if (!_connections.TryGetValue(connectionId, out var session))
        {
            return new LeaveSubscriptionOutcome(true, null, null, [], []);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var normalizedAlias = NormalizeAlias(rawIdentifier);

        lock (session.SyncRoot)
        {
            if (session.IsRevoked)
            {
                return new LeaveSubscriptionOutcome(
                    Allowed: false,
                    ErrorCode: "UNAUTHORIZED",
                    ErrorMessage: "[UNAUTHORIZED] Phiên kết nối thời gian thực đã bị thu hồi.",
                    GroupsToRemove: [],
                    PrunedExpiredGroups: []);
            }

            var prunedExpiredGroups = PruneExpiredInsideLock(session, nowUtc);

            TrackedSubscriptionEntry? matched = null;
            foreach (var sub in session.Subscriptions.Values)
            {
                if (resourceKeyPrefix is not null && !sub.ResourceKey.StartsWith(resourceKeyPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (normalizedAlias is null ||
                    string.Equals(sub.ResourceKey, normalizedAlias, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sub.AliasKey, normalizedAlias, StringComparison.OrdinalIgnoreCase) ||
                    sub.ResourceKey.EndsWith($":{normalizedAlias}", StringComparison.OrdinalIgnoreCase))
                {
                    matched = sub;
                    break;
                }
            }

            if (matched is null)
                return new LeaveSubscriptionOutcome(true, null, null, [], prunedExpiredGroups);

            session.Subscriptions.Remove(matched.ResourceKey);
            var stillReferenced = CollectActiveGroupsExcept(session, matched.ResourceKey);
            var groupsToRemove = matched.AllGroupNames
                .Where(g => !stillReferenced.Contains(g))
                .ToArray();

            return new LeaveSubscriptionOutcome(true, null, null, groupsToRemove, prunedExpiredGroups);
        }
    }

    public IReadOnlyList<EvictedSubscriptionItem> PruneAllExpiredSubscriptions()
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var evicted = new List<EvictedSubscriptionItem>();

        foreach (var (connId, session) in _connections)
        {
            lock (session.SyncRoot)
            {
                var expiredKeys = session.Subscriptions
                    .Where(kvp => kvp.Value.ExpiresAtUtc <= nowUtc)
                    .Select(kvp => kvp.Key)
                    .ToList();

                if (expiredKeys.Count == 0)
                    continue;

                foreach (var key in expiredKeys)
                {
                    var removed = session.Subscriptions[key];
                    session.Subscriptions.Remove(key);
                    var remainingGroups = CollectActiveGroupsExcept(session, null);
                    var orphanGroups = removed.AllGroupNames.Where(g => !remainingGroups.Contains(g)).ToArray();
                    evicted.Add(new EvictedSubscriptionItem(connId, removed.ResourceKey, orphanGroups));
                }
            }
        }

        return evicted;
    }

    public IReadOnlyList<EvictedSubscriptionItem> EvictFamilyMemberSubscriptions(long ownerUserId, long memberPatientId)
    {
        var evicted = new List<EvictedSubscriptionItem>();

        foreach (var (connId, session) in _connections)
        {
            if (session.UserId != ownerUserId)
                continue;

            lock (session.SyncRoot)
            {
                var targetKeys = session.Subscriptions
                    .Where(kvp => kvp.Value.PatientId == memberPatientId)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in targetKeys)
                {
                    var removed = session.Subscriptions[key];
                    session.Subscriptions.Remove(key);
                    var remainingGroups = CollectActiveGroupsExcept(session, null);
                    var orphanGroups = removed.AllGroupNames.Where(g => !remainingGroups.Contains(g)).ToArray();
                    evicted.Add(new EvictedSubscriptionItem(connId, removed.ResourceKey, orphanGroups));
                }
            }
        }

        return evicted;
    }

    public IReadOnlyList<EvictedSubscriptionItem> EvictAppointmentSubscriptions(long appointmentId)
    {
        var evicted = new List<EvictedSubscriptionItem>();

        foreach (var (connId, session) in _connections)
        {
            lock (session.SyncRoot)
            {
                var targetKeys = session.Subscriptions
                    .Where(kvp => kvp.Value.AppointmentId == appointmentId)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in targetKeys)
                {
                    var removed = session.Subscriptions[key];
                    session.Subscriptions.Remove(key);
                    var remainingGroups = CollectActiveGroupsExcept(session, null);
                    var orphanGroups = removed.AllGroupNames.Where(g => !remainingGroups.Contains(g)).ToArray();
                    evicted.Add(new EvictedSubscriptionItem(connId, removed.ResourceKey, orphanGroups));
                }
            }
        }

        return evicted;
    }

    public IReadOnlyList<BucketMigrationItem> MigrateAppointmentBuckets(
        long appointmentId,
        IReadOnlyList<string> newBucketGroups)
    {
        var migrations = new List<BucketMigrationItem>();

        foreach (var (connId, session) in _connections)
        {
            lock (session.SyncRoot)
            {
                var matchingKeys = session.Subscriptions
                    .Where(kvp =>
                        kvp.Value.AppointmentId == appointmentId &&
                        kvp.Key.StartsWith("appointment-queue:", StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in matchingKeys)
                {
                    var current = session.Subscriptions[key];
                    var updated = current with { BucketGroupNames = newBucketGroups };
                    session.Subscriptions[key] = updated;

                    var stillReferenced = CollectActiveGroupsExcept(session, key);
                    var toRemove = current.BucketGroupNames
                        .Where(oldG =>
                            !newBucketGroups.Contains(oldG, StringComparer.Ordinal) &&
                            !stillReferenced.Contains(oldG))
                        .ToArray();
                    var toAdd = newBucketGroups
                        .Where(newG => !current.BucketGroupNames.Contains(newG, StringComparer.Ordinal))
                        .ToArray();

                    migrations.Add(new BucketMigrationItem(connId, appointmentId, toRemove, toAdd));
                }
            }
        }

        return migrations;
    }

    public IReadOnlyList<EvictedSubscriptionItem> EvictUserSubscriptions(long userId)
        => EvictUserSubscriptionsExceptSessionFamily(userId, keepSessionIds: null);

    public IReadOnlyList<EvictedSubscriptionItem> EvictUserSubscriptionsExceptSession(
        long userId,
        long? keepSessionId)
        => EvictUserSubscriptionsExceptSessionFamily(
            userId,
            keepSessionId.HasValue ? [keepSessionId.Value] : null);

    public IReadOnlyList<EvictedSubscriptionItem> EvictUserSubscriptionsExceptSessionFamily(
        long userId,
        IReadOnlyCollection<long>? keepSessionIds)
    {
        var keepSet = keepSessionIds is { Count: > 0 }
            ? keepSessionIds.ToHashSet()
            : null;
        var evicted = new List<EvictedSubscriptionItem>();

        foreach (var (connId, session) in _connections)
        {
            if (session.UserId != userId)
                continue;

            if (keepSet is not null && session.SessionId is long sid && keepSet.Contains(sid))
                continue;

            lock (session.SyncRoot)
            {
                session.IsRevoked = true;
                if (session.Subscriptions.Count == 0)
                {
                    evicted.Add(new EvictedSubscriptionItem(connId, "session", []));
                    continue;
                }

                var allKeys = session.Subscriptions.Keys.ToList();
                foreach (var key in allKeys)
                {
                    var removed = session.Subscriptions[key];
                    session.Subscriptions.Remove(key);
                    evicted.Add(new EvictedSubscriptionItem(connId, removed.ResourceKey, removed.AllGroupNames));
                }
            }
        }

        return evicted;
    }

    public IReadOnlyList<EvictedSubscriptionItem> EvictUserSessionFamilySubscriptions(
        long userId,
        IReadOnlyCollection<long> sessionIds)
    {
        if (sessionIds is null || sessionIds.Count == 0)
            return [];

        var targetSet = sessionIds.ToHashSet();
        var evicted = new List<EvictedSubscriptionItem>();

        foreach (var (connId, session) in _connections)
        {
            if (session.UserId != userId)
                continue;

            if (session.SessionId is not long sid || !targetSet.Contains(sid))
                continue;

            lock (session.SyncRoot)
            {
                session.IsRevoked = true;
                if (session.Subscriptions.Count == 0)
                {
                    evicted.Add(new EvictedSubscriptionItem(connId, "session", []));
                    continue;
                }

                var allKeys = session.Subscriptions.Keys.ToList();
                foreach (var key in allKeys)
                {
                    var removed = session.Subscriptions[key];
                    session.Subscriptions.Remove(key);
                    evicted.Add(new EvictedSubscriptionItem(connId, removed.ResourceKey, removed.AllGroupNames));
                }
            }
        }

        return evicted;
    }

    private static IReadOnlyList<string> PruneExpiredInsideLock(ConnectionSessionState session, DateTime nowUtc)
    {
        var expiredKeys = session.Subscriptions
            .Where(kvp => kvp.Value.ExpiresAtUtc <= nowUtc)
            .Select(kvp => kvp.Key)
            .ToList();

        if (expiredKeys.Count == 0)
            return [];

        var candidateGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in expiredKeys)
        {
            foreach (var g in session.Subscriptions[key].AllGroupNames)
                candidateGroups.Add(g);
            session.Subscriptions.Remove(key);
        }

        var remaining = CollectActiveGroupsExcept(session, null);
        return candidateGroups.Where(g => !remaining.Contains(g)).ToArray();
    }

    private static HashSet<string> CollectActiveGroupsExcept(ConnectionSessionState session, string? excludedKey)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, sub) in session.Subscriptions)
        {
            if (excludedKey is not null && string.Equals(key, excludedKey, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var g in sub.AllGroupNames)
                active.Add(g);
        }

        return active;
    }

    private static string? NormalizeAlias(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }
}
