using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

/// <summary>
/// Enforces the Stage E2 patient-level single-active-serving-queue invariant across (PatientId, HospitalId)
/// (ADR-0061 / E-BR10 / QE-07 / QE-08 / QE-23 / PH-01).
/// Callers MUST acquire Level 02 (<c>TryLockPatientForUpdateAsync</c>) before Level 09 (<c>visits</c>).
/// </summary>
public static class PatientSingleActiveQueueGuard
{
    public const string ActiveQueueAlreadyExistsCode = "ACTIVE_QUEUE_ALREADY_EXISTS";

    public const string ActiveQueueAlreadyExistsMessage =
        "[ACTIVE_QUEUE_ALREADY_EXISTS] Bệnh nhân hiện đang có một lượt xếp hàng/phục vụ chưa hoàn tất tại bệnh viện. Vui lòng hoàn tất hoặc rời hàng đợi hiện tại trước khi lấy số mới.";

    private static readonly HashSet<string> ActiveServingQueueTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "EXAMINATION",
        "DIAGNOSTIC",
        "RETURN_CONSULTATION",
        "PHARMACY",
    };

    private static readonly HashSet<string> ActiveServingQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WAITING",
        "READY",
        "CALLED",
        "IN_PROGRESS",
        "IN_CONSULTATION",
        "SERVING",
    };

    public static bool IsActiveServingQueueEntry(QueueEntry? entry)
    {
        if (entry is null || !entry.VisitId.HasValue || entry.CompletedAt.HasValue)
        {
            return false;
        }

        var qType = string.IsNullOrWhiteSpace(entry.QueueType)
            ? "EXAMINATION"
            : entry.QueueType.Trim().ToUpperInvariant();
        if (!ActiveServingQueueTypes.Contains(qType))
        {
            return false;
        }

        var qStatus = AppointmentBookingRules.NormalizeStatusKey(entry.Status);
        return ActiveServingQueueStatuses.Contains(qStatus);
    }

    public static async Task<QueueEntry?> FindActiveServingQueueForPatientAsync(
        IApplicationDbContext db,
        long patientId,
        long? hospitalId,
        DateOnly? todayLocal = null,
        long? excludeQueueEntryId = null,
        long? excludeAppointmentId = null,
        CancellationToken ct = default)
    {
        if (patientId <= 0)
        {
            return null;
        }

        var patientVisits = await db.Visits
            .AsNoTracking()
            .Where(v => v.PatientId == patientId)
            .ToListAsync(ct);

        if (patientVisits.Count == 0)
        {
            return null;
        }

        var visitById = patientVisits.ToDictionary(v => v.Id);
        var visitIds = visitById.Keys.ToList();

        var candidateEntries = await db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.VisitId.HasValue &&
                visitIds.Contains(q.VisitId.Value) &&
                q.CompletedAt == null)
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        foreach (var entry in candidateEntries)
        {
            if (excludeQueueEntryId.HasValue && entry.Id == excludeQueueEntryId.Value)
            {
                continue;
            }

            if (excludeAppointmentId.HasValue && entry.AppointmentId == excludeAppointmentId.Value)
            {
                continue;
            }

            if (!IsActiveServingQueueEntry(entry))
            {
                continue;
            }

            var visit = visitById[entry.VisitId!.Value];
            var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
            var qType = string.IsNullOrWhiteSpace(entry.QueueType)
                ? "EXAMINATION"
                : entry.QueueType.Trim().ToUpperInvariant();

            // Completed visits can only hold an active PHARMACY queue (QE-19), never stale clinical/CLS queues.
            if (visitStatus is "CANCELLED" or "NO_SHOW" ||
                (visitStatus is "COMPLETED" or "DONE" && qType != "PHARMACY"))
            {
                continue;
            }

            // Unstarted prior-day waiting tickets (not IN_PROGRESS) are rolled over/deferred when resumed;
            // however, on the SAME day or when IN_PROGRESS, or on the database unique index, check hospital scope.
            if (todayLocal.HasValue &&
                entry.QueueDate.HasValue &&
                entry.QueueDate.Value < todayLocal.Value &&
                AppointmentBookingRules.NormalizeStatusKey(entry.Status) is not ("IN_PROGRESS" or "IN_CONSULTATION" or "SERVING"))
            {
                continue;
            }

            if (hospitalId.HasValue && hospitalId.Value > 0)
            {
                var entryHospitalId = await ResolveQueueEntryHospitalIdAsync(db, entry, visit, ct);
                if (entryHospitalId.HasValue && entryHospitalId.Value != hospitalId.Value)
                {
                    continue;
                }
            }

            return entry;
        }

        return null;
    }

    public static async Task EnsureNoOtherActiveServingQueueAsync(
        IApplicationDbContext db,
        long? patientId,
        long? hospitalId,
        DateOnly? todayLocal = null,
        long? excludeQueueEntryId = null,
        long? excludeAppointmentId = null,
        CancellationToken ct = default)
    {
        if (!patientId.HasValue || patientId.Value <= 0)
        {
            return;
        }

        var existing = await FindActiveServingQueueForPatientAsync(
            db,
            patientId.Value,
            hospitalId,
            todayLocal,
            excludeQueueEntryId,
            excludeAppointmentId,
            ct);

        if (existing is not null)
        {
            throw new ConflictException(ActiveQueueAlreadyExistsMessage);
        }
    }

    /// <summary>
    /// Closes stale prior-day (<c>QueueDate &lt; todayLocal</c>) non-<c>IN_PROGRESS</c> queue entries on <paramref name="visitId"/>
    /// as <c>DEFERRED</c> so the PostgreSQL partial unique index <c>ux_queue_entries_one_active_post_exam_per_visit</c>
    /// is never blocked by an unclosed yesterday waiting ticket, while preserving <c>IN_PROGRESS</c> entries intact.
    /// </summary>
    public static int CloseStalePriorDayQueuesOnVisit(
        IEnumerable<QueueEntry> trackedVisitQueues,
        DateOnly todayLocal,
        DateTime nowLocal)
    {
        var closedCount = 0;
        foreach (var q in trackedVisitQueues)
        {
            if (!q.QueueDate.HasValue || q.QueueDate.Value >= todayLocal || q.CompletedAt.HasValue)
            {
                continue;
            }

            var status = AppointmentBookingRules.NormalizeStatusKey(q.Status);
            if (status is "COMPLETED" or "DONE" or "CANCELLED" or "DEFERRED" or "NO_SHOW" or "SKIPPED" or "RESERVED"
                or "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING")
            {
                continue;
            }

            q.Status = "DEFERRED";
            q.PresenceStatus = "LEFT";
            q.CalledAt = null;
            q.CompletedAt = nowLocal;
            q.BlockedReason ??= "Đóng lượt hàng đợi ngày cũ (DEFERRED) khi bước sang ngày phục vụ mới.";
            q.StateVersion = (q.StateVersion is > 0 ? q.StateVersion.Value : 1) + 1;
            closedCount++;
        }

        return closedCount;
    }

    private static async Task<long?> ResolveQueueEntryHospitalIdAsync(
        IApplicationDbContext db,
        QueueEntry entry,
        Visit visit,
        CancellationToken ct)
    {
        var spId = entry.CurrentServicePointId ?? entry.ServicePointId;
        if (spId.HasValue)
        {
            try
            {
                var sp = await db.QueueServicePoints
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == spId.Value, ct);
                if (sp?.HospitalId.HasValue == true)
                {
                    return sp.HospitalId.Value;
                }

                if (sp?.RoomId.HasValue == true)
                {
                    var room = await db.HospitalRooms
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == sp.RoomId.Value, ct);
                    if (room?.HospitalId.HasValue == true)
                    {
                        return room.HospitalId.Value;
                    }
                }
            }
            catch (NotSupportedException)
            {
            }
        }

        try
        {
            var visitHospitals = await VisitHospitalResolver.CollectAsync(db, visit, ct);
            if (visitHospitals.Count == 1)
            {
                return visitHospitals.First();
            }
        }
        catch (NotSupportedException)
        {
        }

        return null;
    }
}
