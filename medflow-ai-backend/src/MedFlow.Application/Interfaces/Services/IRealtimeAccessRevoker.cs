namespace MedFlow.Application.Interfaces.Services;

public interface IRealtimeAccessRevoker
{
    Task RevokeFamilyMemberAccessAsync(
        long ownerUserId,
        long memberPatientId,
        CancellationToken ct = default);

    Task RevokeAppointmentTrackingAsync(
        long appointmentId,
        CancellationToken ct = default);

    Task MigrateAppointmentBucketAsync(
        long appointmentId,
        long? newServicePointId,
        long? newRoomId,
        CancellationToken ct = default);

    Task RevokeUserConnectionsAsync(
        long userId,
        string reasonCode = "ACCOUNT_REVOKED",
        CancellationToken ct = default);

    Task RevokeUserConnectionsExceptSessionAsync(
        long userId,
        long? keepSessionId,
        string reasonCode = "SESSION_REVOKED",
        CancellationToken ct = default)
        => keepSessionId.HasValue
            ? Task.CompletedTask
            : RevokeUserConnectionsAsync(userId, reasonCode, ct);
}
