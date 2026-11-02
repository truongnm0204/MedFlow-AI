namespace MedFlow.Application.Interfaces.Persistence;

/// <summary>
/// Row-locks (SELECT ... FOR UPDATE) patient → doctor → slot in that fixed order.
/// Must be called inside a transaction opened by IApplicationDbContext.BeginTransactionAsync on the same scoped context.
/// </summary>
public interface IAppointmentBookingLock
{
    Task AcquireAsync(long patientId, long? doctorId, long slotId, CancellationToken ct = default);
}
