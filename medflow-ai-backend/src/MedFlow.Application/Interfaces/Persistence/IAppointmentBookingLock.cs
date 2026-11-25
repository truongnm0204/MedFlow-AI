namespace MedFlow.Application.Interfaces.Persistence;

/// <summary>
/// Row-locks (SELECT ... FOR UPDATE) scheduling resources in strict ADR-0059 hierarchy order:
/// patients (02) -> services (04) -> doctors (05, ASC) -> hospital_rooms (06, ASC) -> examination_slots (07, ASC).
/// Must be called inside an active transaction opened by IApplicationDbContext.BeginTransactionAsync.
/// </summary>
public interface IAppointmentBookingLock
{
    Task AcquireAsync(long patientId, long? doctorId, long slotId, CancellationToken ct = default);

    Task AcquireBatchAsync(
        long patientId,
        long? serviceId,
        IReadOnlyCollection<long> doctorIds,
        IReadOnlyCollection<long> roomIds,
        IReadOnlyCollection<long> slotIds,
        CancellationToken ct = default)
        => AcquireAsync(
            patientId,
            doctorIds.Count > 0 ? doctorIds.Min() : null,
            slotIds.First(),
            ct);
}
