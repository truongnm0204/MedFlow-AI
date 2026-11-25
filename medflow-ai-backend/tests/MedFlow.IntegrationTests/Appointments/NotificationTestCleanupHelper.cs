using System.Globalization;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Appointments;

internal static class NotificationTestCleanupHelper
{
    public static async Task CleanupSlotsAndAppointmentsAsync(
        MedFlowDbContext db,
        params long[] slotIds)
    {
        if (slotIds.Length == 0)
        {
            return;
        }

        var apptIds = await db.Appointments
            .Where(a => a.SlotId != null && slotIds.Contains(a.SlotId.Value))
            .Select(a => a.Id)
            .ToListAsync();

        await CleanupAppointmentDependentsAsync(db, apptIds);

        await db.Appointments
            .Where(a => a.SlotId != null && slotIds.Contains(a.SlotId.Value))
            .ExecuteDeleteAsync();

        await db.ExaminationSlots
            .Where(s => slotIds.Contains(s.Id))
            .ExecuteDeleteAsync();
    }

    public static async Task CleanupAppointmentDependentsAsync(
        MedFlowDbContext db,
        IReadOnlyCollection<long> appointmentIds)
    {
        if (appointmentIds.Count == 0)
        {
            return;
        }

        var apptIdStrings = appointmentIds
            .Select(id => id.ToString(CultureInfo.InvariantCulture))
            .ToList();

        var queueIds = await db.QueueEntries
            .Where(q => q.AppointmentId != null && appointmentIds.Contains(q.AppointmentId.Value))
            .Select(q => q.Id)
            .ToListAsync();

        if (queueIds.Count > 0)
        {
            var queueIdStrings = queueIds
                .Select(id => id.ToString(CultureInfo.InvariantCulture))
                .ToList();

            await db.AuditLogs
                .Where(l => l.EntityType == "QUEUE_ENTRY" && l.EntityId != null && queueIdStrings.Contains(l.EntityId))
                .ExecuteDeleteAsync();

            await db.QueueEntries
                .Where(q => queueIds.Contains(q.Id))
                .ExecuteDeleteAsync();
        }

        await db.Notifications
            .Where(n => n.AppointmentId != null && appointmentIds.Contains(n.AppointmentId.Value))
            .ExecuteDeleteAsync();

        await db.AuditLogs
            .Where(l => l.EntityType == "APPOINTMENT" && l.EntityId != null && apptIdStrings.Contains(l.EntityId))
            .ExecuteDeleteAsync();
    }
}
