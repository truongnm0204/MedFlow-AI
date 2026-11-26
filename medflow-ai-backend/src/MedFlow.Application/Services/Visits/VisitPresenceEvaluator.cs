using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;

namespace MedFlow.Application.Services.Visits;

/// <summary>
/// Evaluates durable daily patient presence and monotonic presence sessions in the hospital
/// for cross-day and Stage E2 Queue-When-Ready workflows (ADR-0060 / ADR-0061).
/// Never infers presence merely because a Visit is open, paid, or a DiagnosticResult was finalized.
/// </summary>
public static class VisitPresenceEvaluator
{
    public static bool IsPresentToday(
        Visit? visit,
        IEnumerable<QueueEntry>? visitQueueEntries,
        DateOnly todayLocal,
        DateTime? nowLocal = null,
        int? presenceTtlMinutes = null)
    {
        if (visit is null)
        {
            return false;
        }

        var presenceState = NormalizePresenceState(visit.PresenceState);

        // 1. Explicit durable departure or deferral stamped today overrides everything until staff re-confirms presence.
        if (visit.LastPresentDate.HasValue &&
            visit.LastPresentDate.Value == todayLocal &&
            presenceState is VisitPresenceStates.Left or VisitPresenceStates.Deferred)
        {
            return false;
        }

        var hasActiveTodayQueue = visitQueueEntries is not null &&
                                  visitQueueEntries.Any(qe => IsActivePresentQueueEntryToday(qe, todayLocal));

        // 2. Durable presence stamped today by Check-in, Walk-in, Diagnostic/Pharmacy Staff, or Reception Resume.
        if (visit.LastPresentDate.HasValue &&
            visit.LastPresentDate.Value == todayLocal &&
            presenceState == VisitPresenceStates.Present)
        {
            if (presenceTtlMinutes is > 0 && nowLocal.HasValue && !hasActiveTodayQueue)
            {
                var anchor = visit.LastResumedAt ?? visit.CheckedInAt;
                if (anchor.HasValue &&
                    DateOnly.FromDateTime(anchor.Value) == todayLocal &&
                    nowLocal.Value > anchor.Value.AddMinutes(presenceTtlMinutes.Value))
                {
                    return false;
                }
            }

            return true;
        }

        // 3. Backwards-compatible same-day fallback for tests seeding Visit directly without LastPresentDate
        //    or seeding an active today QueueEntry with PresenceStatus == PRESENT.
        if (!visit.LastPresentDate.HasValue &&
            visit.CheckedInAt.HasValue &&
            DateOnly.FromDateTime(visit.CheckedInAt.Value) == todayLocal &&
            presenceState is not (VisitPresenceStates.Left or VisitPresenceStates.Deferred))
        {
            if (presenceTtlMinutes is > 0 && nowLocal.HasValue && !hasActiveTodayQueue)
            {
                var anchor = visit.LastResumedAt ?? visit.CheckedInAt.Value;
                if (nowLocal.Value > anchor.AddMinutes(presenceTtlMinutes.Value))
                {
                    return false;
                }
            }

            return true;
        }

        if (hasActiveTodayQueue)
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

        if (visit.PresenceSessionNo < 1)
        {
            visit.PresenceSessionNo = 1;
        }

        var prevState = NormalizePresenceState(visit.PresenceState);
        var wasPriorDay = visit.LastPresentDate.HasValue
            ? visit.LastPresentDate.Value < todayLocal
            : (visit.CheckedInAt.HasValue && DateOnly.FromDateTime(visit.CheckedInAt.Value) < todayLocal);
        var wasLeftOrDeferred = prevState is VisitPresenceStates.Left or VisitPresenceStates.Deferred;

        if (wasPriorDay || wasLeftOrDeferred)
        {
            visit.PresenceSessionNo = Math.Max(1, visit.PresenceSessionNo) + 1;
        }

        visit.LastPresentDate = todayLocal;
        visit.PresenceState = VisitPresenceStates.Present;
        if (resumedAtLocal.HasValue)
        {
            visit.LastResumedAt = resumedAtLocal.Value;
        }
    }

    public static void StampDepartedOrDeferred(Visit visit, DateOnly todayLocal, bool isDeferred)
    {
        ArgumentNullException.ThrowIfNull(visit);

        visit.LastPresentDate = todayLocal;
        visit.PresenceState = isDeferred ? VisitPresenceStates.Deferred : VisitPresenceStates.Left;
        visit.PresenceSessionNo = Math.Max(1, visit.PresenceSessionNo) + 1;
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
        if (status is "CANCELLED" or "DEFERRED" or "COMPLETED" or "DONE" or "NO_SHOW" or "SKIPPED" or "RESERVED")
        {
            return false;
        }

        var presence = AppointmentBookingRules.NormalizeStatusKey(qe.PresenceStatus);
        return presence == VisitPresenceStates.Present;
    }
}
