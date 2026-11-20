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
        var all = await CollectManyAsync(db, [visit], ct);
        return all.TryGetValue(visit.Id, out var ids) ? ids : [];
    }

    /// <summary>
    /// Resolve hospital cho cả trang visit bằng số truy vấn cố định (tránh N+1).
    /// Cùng quy tắc với <see cref="CollectAsync"/>.
    /// </summary>
    public static async Task<Dictionary<long, HashSet<long>>> CollectManyAsync(
        IApplicationDbContext db,
        IReadOnlyList<Visit> visits,
        CancellationToken ct)
    {
        var result = new Dictionary<long, HashSet<long>>(visits.Count);
        foreach (var visit in visits)
            result[visit.Id] = [];

        if (visits.Count == 0)
            return result;

        var visitIds = visits.Select(v => v.Id).ToList();

        var slotByAppointment = new Dictionary<long, long>();
        var appointmentIds = DistinctIds(visits.Select(v => v.AppointmentId));
        if (appointmentIds.Count > 0)
        {
            var rows = await db.Appointments.AsNoTracking()
                .Where(a => appointmentIds.Contains(a.Id))
                .Select(a => new { a.Id, a.SlotId })
                .ToListAsync(ct);
            foreach (var row in rows)
                if (row.SlotId is long slotId)
                    slotByAppointment[row.Id] = slotId;
        }

        var slotById = new Dictionary<long, ExamSlot>();
        if (slotByAppointment.Count > 0)
        {
            var slotIds = slotByAppointment.Values.Distinct().ToList();
            var rows = await db.ExaminationSlots.AsNoTracking()
                .Where(s => slotIds.Contains(s.Id))
                .Select(s => new { s.Id, s.ServiceId, s.DoctorId })
                .ToListAsync(ct);
            foreach (var row in rows)
                slotById[row.Id] = new ExamSlot(row.ServiceId, row.DoctorId);
        }

        var serviceIds = DistinctIds(slotById.Values.Select(s => s.ServiceId));
        var doctorIds = DistinctIds(
            slotById.Values.Select(s => s.DoctorId).Concat(visits.Select(v => v.AssignedDoctorId)));
        var roomIds = DistinctIds(visits.Select(v => v.ExaminationRoomId));
        var staffIds = DistinctIds(visits.Select(v => v.CheckedInByStaffId));

        var serviceHospitals = new Dictionary<long, long?>();
        if (serviceIds.Count > 0)
        {
            var rows = await db.Services.AsNoTracking()
                .Where(s => serviceIds.Contains(s.Id))
                .Select(s => new { s.Id, s.HospitalId })
                .ToListAsync(ct);
            foreach (var row in rows)
                serviceHospitals[row.Id] = row.HospitalId;
        }

        var doctorHospitals = new Dictionary<long, long?>();
        if (doctorIds.Count > 0)
        {
            var rows = await db.Doctors.AsNoTracking()
                .Where(d => doctorIds.Contains(d.Id))
                .Select(d => new { d.Id, d.HospitalId })
                .ToListAsync(ct);
            foreach (var row in rows)
                doctorHospitals[row.Id] = row.HospitalId;
        }

        var roomHospitals = new Dictionary<long, long?>();
        if (roomIds.Count > 0)
        {
            var rows = await db.HospitalRooms.AsNoTracking()
                .Where(r => roomIds.Contains(r.Id))
                .Select(r => new { r.Id, r.HospitalId })
                .ToListAsync(ct);
            foreach (var row in rows)
                roomHospitals[row.Id] = row.HospitalId;
        }

        var staffHospitals = new Dictionary<long, long?>();
        if (staffIds.Count > 0)
        {
            var rows = await db.Staff.AsNoTracking()
                .Where(s => staffIds.Contains(s.Id))
                .Select(s => new { s.Id, s.HospitalId })
                .ToListAsync(ct);
            foreach (var row in rows)
                staffHospitals[row.Id] = row.HospitalId;
        }

        var entriesByVisit = new Dictionary<long, List<(long? ServicePointId, long? CurrentServicePointId)>>();
        var pointHospitals = new Dictionary<long, long?>();
        var entryRows = await db.QueueEntries.AsNoTracking()
            .Where(q => q.VisitId != null && visitIds.Contains(q.VisitId.Value))
            .Select(q => new { q.VisitId, q.ServicePointId, q.CurrentServicePointId })
            .ToListAsync(ct);
        foreach (var row in entryRows)
        {
            if (row.VisitId is not long visitId)
                continue;
            if (!entriesByVisit.TryGetValue(visitId, out var list))
                entriesByVisit[visitId] = list = [];
            list.Add((row.ServicePointId, row.CurrentServicePointId));
        }

        var pointIds = DistinctIds(entryRows
            .SelectMany(e => new long?[] { e.ServicePointId, e.CurrentServicePointId }));
        if (pointIds.Count > 0)
        {
            var rows = await db.QueueServicePoints.AsNoTracking()
                .Where(p => pointIds.Contains(p.Id))
                .Select(p => new { p.Id, p.HospitalId })
                .ToListAsync(ct);
            foreach (var row in rows)
                pointHospitals[row.Id] = row.HospitalId;
        }

        var auditsByVisit = await VisitWalkInAudits.LoadManyAsync(db, visitIds, ct);

        foreach (var visit in visits)
        {
            var ids = result[visit.Id];
            var slot = ResolveSlot(visit, slotByAppointment, slotById);
            if (slot is not null)
            {
                AddMapped(ids, serviceHospitals, slot.ServiceId);
                AddMapped(ids, doctorHospitals, slot.DoctorId);
            }

            if (auditsByVisit.TryGetValue(visit.Id, out var audits))
            {
                foreach (var audit in audits)
                    AddIfPresent(ids, audit.HospitalId);
            }

            AddMapped(ids, roomHospitals, visit.ExaminationRoomId);
            AddMapped(ids, doctorHospitals, visit.AssignedDoctorId);
            AddMapped(ids, staffHospitals, visit.CheckedInByStaffId);

            if (entriesByVisit.TryGetValue(visit.Id, out var entries))
            {
                foreach (var entry in entries)
                {
                    AddMapped(ids, pointHospitals, entry.ServicePointId);
                    AddMapped(ids, pointHospitals, entry.CurrentServicePointId);
                }
            }
        }

        return result;
    }

    private static ExamSlot? ResolveSlot(
        Visit visit,
        Dictionary<long, long> slotByAppointment,
        Dictionary<long, ExamSlot> slotById)
    {
        if (visit.AppointmentId is not long appointmentId
            || !slotByAppointment.TryGetValue(appointmentId, out var slotId)
            || !slotById.TryGetValue(slotId, out var slot))
            return null;
        return slot;
    }

    private static List<long> DistinctIds(IEnumerable<long?> source) =>
        source.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

    private static void AddMapped(
        HashSet<long> ids,
        IReadOnlyDictionary<long, long?> map,
        long? key)
    {
        if (key is long value && map.TryGetValue(value, out var hospitalId))
            AddIfPresent(ids, hospitalId);
    }

    private static void AddIfPresent(HashSet<long> ids, long? hospitalId)
    {
        if (hospitalId is > 0)
            ids.Add(hospitalId.Value);
    }

    private sealed record ExamSlot(long? ServiceId, long? DoctorId);
}
