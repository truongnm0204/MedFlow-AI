using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

internal static class VisitHospitalResolver
{
    public static async Task<HashSet<long>> CollectAsync(
        IApplicationDbContext db,
        Visit visit,
        CancellationToken ct)
    {
        var ids = new HashSet<long>();

        if (visit.AppointmentId is long appointmentId)
        {
            var appointment = await db.Appointments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
            if (appointment?.SlotId is long slotId)
            {
                var slot = await db.ExaminationSlots.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == slotId, ct);
                if (slot?.ServiceId is long serviceId)
                {
                    var serviceHospital = await db.Services.AsNoTracking()
                        .Where(s => s.Id == serviceId)
                        .Select(s => s.HospitalId)
                        .FirstOrDefaultAsync(ct);
                    AddIfPresent(ids, serviceHospital);
                }

                if (slot?.DoctorId is long doctorId)
                {
                    var doctorHospital = await db.Doctors.AsNoTracking()
                        .Where(d => d.Id == doctorId)
                        .Select(d => d.HospitalId)
                        .FirstOrDefaultAsync(ct);
                    AddIfPresent(ids, doctorHospital);
                }
            }
        }

        var audits = await VisitWalkInAudits.LoadAsync(db, visit.Id, ct);
        foreach (var audit in audits)
            AddIfPresent(ids, audit.HospitalId);

        if (visit.ExaminationRoomId is long roomId)
        {
            var roomHospital = await db.HospitalRooms.AsNoTracking()
                .Where(r => r.Id == roomId)
                .Select(r => r.HospitalId)
                .FirstOrDefaultAsync(ct);
            AddIfPresent(ids, roomHospital);
        }

        if (visit.AssignedDoctorId is long assignedDoctorId)
        {
            var assignedHospital = await db.Doctors.AsNoTracking()
                .Where(d => d.Id == assignedDoctorId)
                .Select(d => d.HospitalId)
                .FirstOrDefaultAsync(ct);
            AddIfPresent(ids, assignedHospital);
        }

        if (visit.CheckedInByStaffId is long staffId)
        {
            var staffHospital = await db.Staff.AsNoTracking()
                .Where(s => s.Id == staffId)
                .Select(s => s.HospitalId)
                .FirstOrDefaultAsync(ct);
            AddIfPresent(ids, staffHospital);
        }

        var entries = await db.QueueEntries.AsNoTracking()
            .Where(q => q.VisitId == visit.Id)
            .ToListAsync(ct);

        var pointIds = entries
            .SelectMany(q => new long?[] { q.ServicePointId, q.CurrentServicePointId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (pointIds.Count > 0)
        {
            var pointHospitals = await db.QueueServicePoints.AsNoTracking()
                .Where(p => pointIds.Contains(p.Id))
                .Select(p => p.HospitalId)
                .ToListAsync(ct);
            foreach (var hospitalId in pointHospitals)
                AddIfPresent(ids, hospitalId);
        }

        return ids;
    }

    private static void AddIfPresent(HashSet<long> ids, long? hospitalId)
    {
        if (hospitalId is > 0)
            ids.Add(hospitalId.Value);
    }
}
