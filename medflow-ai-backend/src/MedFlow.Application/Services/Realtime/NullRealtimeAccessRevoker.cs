using MedFlow.Application.Interfaces.Services;

namespace MedFlow.Application.Services.Realtime;

public sealed class NullRealtimeAccessRevoker : IRealtimeAccessRevoker
{
    public static readonly NullRealtimeAccessRevoker Instance = new();

    public Task RevokeFamilyMemberAccessAsync(
        long ownerUserId,
        long memberPatientId,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task RevokeAppointmentTrackingAsync(
        long appointmentId,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task MigrateAppointmentBucketAsync(
        long appointmentId,
        long? newServicePointId,
        long? newRoomId,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task RevokeUserConnectionsAsync(
        long userId,
        string reasonCode = "ACCOUNT_REVOKED",
        CancellationToken ct = default) => Task.CompletedTask;

    public Task RevokeUserConnectionsExceptSessionAsync(
        long userId,
        long? keepSessionId,
        string reasonCode = "SESSION_REVOKED",
        CancellationToken ct = default) => Task.CompletedTask;
}

public static class RealtimeRevokeSafeExtensions
{
    public static async Task TryRevokeFamilyMemberAccessSafeAsync(
        this IRealtimeAccessRevoker revoker,
        long ownerUserId,
        long memberPatientId,
        CancellationToken ct = default)
    {
        try
        {
            await revoker.RevokeFamilyMemberAccessAsync(ownerUserId, memberPatientId, ct);
        }
        catch
        {
            // Best-effort in-memory group eviction; lease TTL remains final backstop.
        }
    }

    public static async Task TryRevokeAppointmentTrackingSafeAsync(
        this IRealtimeAccessRevoker revoker,
        long appointmentId,
        CancellationToken ct = default)
    {
        try
        {
            await revoker.RevokeAppointmentTrackingAsync(appointmentId, ct);
        }
        catch
        {
            // Best-effort in-memory group eviction.
        }
    }

    public static async Task TryMigrateAppointmentBucketSafeAsync(
        this IRealtimeAccessRevoker revoker,
        long appointmentId,
        long? newServicePointId,
        long? newRoomId,
        CancellationToken ct = default)
    {
        try
        {
            await revoker.MigrateAppointmentBucketAsync(appointmentId, newServicePointId, newRoomId, ct);
        }
        catch
        {
            // Best-effort bucket migration.
        }
    }

    public static async Task TryRevokeUserConnectionsSafeAsync(
        this IRealtimeAccessRevoker revoker,
        long userId,
        string reasonCode = "ACCOUNT_REVOKED",
        CancellationToken ct = default)
    {
        try
        {
            await revoker.RevokeUserConnectionsAsync(userId, reasonCode, ct);
        }
        catch
        {
            // Best-effort session eviction.
        }
    }

    public static async Task TryRevokeUserConnectionsExceptSessionSafeAsync(
        this IRealtimeAccessRevoker revoker,
        long userId,
        long? keepSessionId,
        string reasonCode = "SESSION_REVOKED",
        CancellationToken ct = default)
    {
        try
        {
            await revoker.RevokeUserConnectionsExceptSessionAsync(userId, keepSessionId, reasonCode, ct);
        }
        catch
        {
            // Best-effort selective session eviction.
        }
    }
}
