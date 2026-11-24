namespace MedFlow.Application.Common.Time;

public enum HospitalShift
{
    Morning,
    Afternoon,
    EveningOnCall,
}

/// <summary>
/// Canonical shift windows and grouping helpers for MedFlow outpatient and diagnostic workflows.
/// <para>
/// Distinguishes between:
/// <list type="bullet">
/// <item><see cref="ResolveShift(TimeOnly, bool)"/>: groups slots/queues/headers into shift buckets.</item>
/// <item><see cref="IsWithinWorkingHours(TimeOnly, bool)"/>: checks whether a local time falls inside active serving windows (excluding inter-shift breaks and off-hours).</item>
/// </list>
/// </para>
/// <para>
/// Architectural invariant: <c>ExaminationSlots</c> in PostgreSQL remain the source of truth for bookable schedules,
/// and shift boundaries must never auto-cancel visits or block ongoing clinical/diagnostic completion.
/// </para>
/// </summary>
public static class HospitalShiftRules
{
    public static readonly TimeOnly MorningStart = new(7, 30);
    public static readonly TimeOnly MorningEnd = new(11, 30);

    public static readonly TimeOnly AfternoonGroupingCutoff = new(12, 0);
    public static readonly TimeSpan AfternoonCutoffSpan = new(12, 0, 0);

    public static readonly TimeOnly AfternoonStart = new(13, 30);
    public static readonly TimeOnly AfternoonEnd = new(16, 30);

    public static readonly TimeOnly EveningGroupingCutoff = new(17, 0);
    public static readonly TimeOnly EveningOnCallStart = new(17, 0);
    public static readonly TimeOnly EveningOnCallEnd = new(21, 0);

    public const string MorningTimeRange = "07:30 - 11:30";
    public const string AfternoonTimeRange = "13:30 - 16:30";
    public const string EveningOnCallTimeRange = "17:00 - 21:00";

    public const string MorningQueueShiftLabel = "Ca sáng (07:30 - 11:30)";
    public const string AfternoonQueueShiftLabel = "Ca chiều (13:30 - 16:30)";
    public const string EveningOnCallQueueShiftLabel = "Ca trực tối (17:00 - 21:00)";

    public const string MorningDashboardShiftLabel = "Ca sáng 07:30–11:30";
    public const string AfternoonDashboardShiftLabel = "Ca chiều 13:30–16:30";
    public const string EveningDashboardShiftLabel = "Ca trực tối 17:00–21:00";

    /// <summary>
    /// Resolves the logical shift bucket used to group slots, queues, and workbench headers.
    /// </summary>
    public static HospitalShift ResolveShift(TimeOnly localTime, bool includeDiagnosticEvening = false)
    {
        if (localTime < AfternoonGroupingCutoff)
        {
            return HospitalShift.Morning;
        }

        if (includeDiagnosticEvening && localTime >= EveningGroupingCutoff)
        {
            return HospitalShift.EveningOnCall;
        }

        return HospitalShift.Afternoon;
    }

    public static HospitalShift ResolveShift(DateTime hospitalLocalNow, bool includeDiagnosticEvening = false)
        => ResolveShift(TimeOnly.FromDateTime(hospitalLocalNow), includeDiagnosticEvening);

    public static string ResolveShiftCode(TimeOnly localTime, bool includeDiagnosticEvening = false)
        => ResolveShift(localTime, includeDiagnosticEvening) switch
        {
            HospitalShift.Morning => "MORNING",
            HospitalShift.EveningOnCall => "EVENING",
            _ => "AFTERNOON",
        };

    public static string ResolveQueueShiftLabel(DateTime hospitalLocalNow, bool includeDiagnosticEvening = false)
        => ResolveShift(hospitalLocalNow, includeDiagnosticEvening) switch
        {
            HospitalShift.Morning => MorningQueueShiftLabel,
            HospitalShift.EveningOnCall => EveningOnCallQueueShiftLabel,
            _ => AfternoonQueueShiftLabel,
        };

    public static string ResolveDashboardShiftLabel(DateTime hospitalLocalNow, bool includeDiagnosticEvening = false)
        => ResolveShift(hospitalLocalNow, includeDiagnosticEvening) switch
        {
            HospitalShift.Morning => MorningDashboardShiftLabel,
            HospitalShift.EveningOnCall => EveningDashboardShiftLabel,
            _ => AfternoonDashboardShiftLabel,
        };

    /// <summary>
    /// Checks whether <paramref name="localTime"/> falls strictly within standard active serving windows
    /// (Morning 07:30–11:30, Afternoon 13:30–16:30, and optional Diagnostic Evening 17:00–21:00).
    /// Midday break (11:31–13:29), handover gap (16:31–16:59), early morning (&lt;07:30), and late night (&gt;21:00) return <c>false</c>.
    /// </summary>
    public static bool IsWithinWorkingHours(
        TimeOnly localTime,
        bool includeDiagnosticEvening = false)
        => TryGetActiveServingShift(localTime, includeDiagnosticEvening, out _);

    public static bool IsWithinWorkingHours(
        DateTime hospitalLocalNow,
        bool includeDiagnosticEvening = false)
        => IsWithinWorkingHours(TimeOnly.FromDateTime(hospitalLocalNow), includeDiagnosticEvening);

    public static bool TryGetActiveServingShift(
        TimeOnly localTime,
        bool includeDiagnosticEvening,
        out HospitalShift activeShift)
    {
        if (localTime >= MorningStart && localTime <= MorningEnd)
        {
            activeShift = HospitalShift.Morning;
            return true;
        }

        if (localTime >= AfternoonStart && localTime <= AfternoonEnd)
        {
            activeShift = HospitalShift.Afternoon;
            return true;
        }

        if (includeDiagnosticEvening &&
            localTime >= EveningOnCallStart &&
            localTime <= EveningOnCallEnd)
        {
            activeShift = HospitalShift.EveningOnCall;
            return true;
        }

        activeShift = default;
        return false;
    }
}
