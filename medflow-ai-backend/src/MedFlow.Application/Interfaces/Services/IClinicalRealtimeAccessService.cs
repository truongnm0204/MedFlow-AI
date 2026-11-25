using MedFlow.Application.DTOs.Realtime;

namespace MedFlow.Application.Interfaces.Services;

public interface IClinicalRealtimeAccessService
{
    Task EnsureConnectedUserActiveAsync(
        long userId,
        CancellationToken ct = default);

    Task<AuthorizedRealtimeSubscription> AuthorizeVisitPaymentJoinAsync(
        long userId,
        string? visitIdOrCode,
        CancellationToken ct = default);

    Task<AuthorizedRealtimeSubscription> AuthorizeAppointmentQueueJoinAsync(
        long userId,
        string? appointmentIdOrCode,
        CancellationToken ct = default);

    Task<AuthorizedRealtimeSubscription> AuthorizeServicePointJoinAsync(
        long userId,
        string? servicePointId,
        CancellationToken ct = default);

    Task<AuthorizedRealtimeSubscription> AuthorizeHospitalQueueJoinAsync(
        long userId,
        CancellationToken ct = default);

    string ResolveVisitPaymentResourceKey(string? visitIdOrCode, long? knownVisitId = null);
}
