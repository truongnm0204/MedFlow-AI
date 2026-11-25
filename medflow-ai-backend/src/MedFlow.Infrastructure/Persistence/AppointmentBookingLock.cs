using MedFlow.Application.Interfaces.Persistence;

namespace MedFlow.Infrastructure.Persistence;

internal sealed class AppointmentBookingLock(MedFlowDbContext db) : IAppointmentBookingLock
{
    public Task AcquireAsync(long patientId, long? doctorId, long slotId, CancellationToken ct = default)
        => AcquireBatchAsync(
            patientId,
            serviceId: null,
            doctorIds: doctorId.HasValue ? [doctorId.Value] : [],
            roomIds: [],
            slotIds: [slotId],
            ct);

    public async Task AcquireBatchAsync(
        long patientId,
        long? serviceId,
        IReadOnlyCollection<long> doctorIds,
        IReadOnlyCollection<long> roomIds,
        IReadOnlyCollection<long> slotIds,
        CancellationToken ct = default)
    {
        // ADR-0059 strict order: patients (02) -> services (04) -> doctors (05, ASC) -> hospital_rooms (06, ASC) -> examination_slots (07, ASC)
        await db.TryLockPatientForUpdateAsync(patientId, ct);

        if (serviceId.HasValue)
        {
            await db.TryLockServiceForUpdateAsync(serviceId.Value, ct);
        }

        if (doctorIds.Count > 0)
        {
            await db.TryLockDoctorsForUpdateAsync(doctorIds, ct);
        }

        if (roomIds.Count > 0)
        {
            await db.TryLockRoomsForUpdateAsync(roomIds, ct);
        }

        if (slotIds.Count > 0)
        {
            await db.TryLockSlotsForUpdateAsync(slotIds, ct);
        }
    }
}
