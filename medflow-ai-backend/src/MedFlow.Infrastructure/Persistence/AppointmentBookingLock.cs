using MedFlow.Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Infrastructure.Persistence;

internal sealed class AppointmentBookingLock(MedFlowDbContext db) : IAppointmentBookingLock
{
    public async Task AcquireAsync(long patientId, long? doctorId, long slotId, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM patients WHERE id = {patientId} FOR UPDATE",
            ct);

        if (doctorId.HasValue)
        {
            var docId = doctorId.Value;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM doctors WHERE id = {docId} FOR UPDATE",
                ct);
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM examination_slots WHERE id = {slotId} FOR UPDATE",
            ct);
    }
}
