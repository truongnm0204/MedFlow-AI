using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;

namespace MedFlow.Application.Services.Visits;

/// <summary>
/// Evaluates durable daily patient presence in the hospital for Stage C cross-day visit workflows (ADR-0060).
/// Never infers presence merely because a Visit is open or a DiagnosticResult was finalized.
/// </summary>
public static class VisitPresenceEvaluator
{
    public static bool IsPresentToday(
        Visit? visit,
        IEnumerable<QueueEntry>? visitQueueEntries,
        DateOnly todayLocal)
    {
        if (visit is null)
        {
            return false;
        }

        var presenceState = NormalizePresenceState(visit.PresenceState);

        // 1. Explicit durable departure or deferral stamped today overrides everything until Reception Resume.
        if (visit.LastPresentDate.HasValue &&
            visit.LastPresentDate.Value == todayLocal &&
            presenceState is VisitPresenceStates.Left or VisitPresenceStates.Deferred)
        {
            return false;
        }

        // 2. Durable presence stamped today by Check-in, Walk-in, Diagnostic Receive, or Reception Resume.
        if (visit.LastPresentDate.HasValue &&
            visit.LastPresentDate.Value == todayLocal &&
            presenceState == VisitPresenceStates.Present)
        {
            return true;
        }

        // 3. Backwards-compatible same-day fallback for tests seeding Visit directly without LastPresentDate
        //    or seeding an active today QueueEntry with PresenceStatus == PRESENT.
        if (!visit.LastPresentDate.HasValue &&
            visit.CheckedInAt.HasValue &&
            DateOnly.FromDateTime(visit.CheckedInAt.Value) == todayLocal &&
            presenceState is not (VisitPresenceStates.Left or VisitPresenceStates.Deferred))
        {
            return true;
        }

        if (visitQueueEntries is not null &&
            visitQueueEntries.Any(qe => IsActivePresentQueueEntryToday(qe, todayLocal)))
        {
            return true;
        }

        // 4. Prior-day visit (CheckedInAt / LastPresentDate < todayLocal) that has not resumed today.
        return false;
    }

    public static string NormalizePresenceState(string? rawState)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(rawState);
        return normalized switch
        {
            VisitPresenceStates.Left => VisitPresenceStates.Left,
            VisitPresenceStates.Deferred => VisitPresenceStates.Deferred,
            _ => VisitPresenceStates.Present,
        };
    }

    public static void StampPresentToday(Visit visit, DateOnly todayLocal, DateTime? resumedAtLocal = null)
    {
        ArgumentNullException.ThrowIfNull(visit);
        visit.LastPresentDate = todayLocal;
        visit.PresenceState = VisitPresenceStates.Present;
        if (resumedAtLocal.HasValue)
        {
            visit.LastResumedAt = resumedAtLocal.Value;
        }
    }

    private static bool IsActivePresentQueueEntryToday(QueueEntry? qe, DateOnly todayLocal)
    {
        if (qe is null || !qe.QueueDate.HasValue || qe.QueueDate.Value != todayLocal)
        {
            return false;
        }

        if (qe.CompletedAt.HasValue)
        {
            return false;
        }

        var status = AppointmentBookingRules.NormalizeStatusKey(qe.Status);
        if (status is "CANCELLED" or "DEFERRED" or "COMPLETED" or "DONE" or "NO_SHOW")
        {
            return false;
        }

        var presence = AppointmentBookingRules.NormalizeStatusKey(qe.PresenceStatus);
        return presence == VisitPresenceStates.Present;
    }
}
