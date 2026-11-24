namespace MedFlow.Application.Common.Time;

/// <summary>
/// Unified hospital clock anchored to Vietnam time (<c>Asia/Ho_Chi_Minh</c>, UTC+7)
/// and driven exclusively through <see cref="TimeProvider"/> so behavior is deterministic
/// across Windows, Linux/Docker UTC containers, and unit tests.
/// </summary>
public static class HospitalClock
{
    public const string IanaTimeZoneId = "Asia/Ho_Chi_Minh";
    private const string WindowsFallbackTimeZoneId = "SE Asia Standard Time";
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);

    public static readonly TimeZoneInfo TimeZone = ResolveTimeZone();

    public static DateTime GetUtcNow(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return timeProvider.GetUtcNow().UtcDateTime;
    }

    public static DateTime GetUtcNowUnspecified(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
    }

    public static DateTimeOffset GetNowOffset(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZone);
    }

    /// <summary>
    /// Returns current hospital wall-clock time (<c>Asia/Ho_Chi_Minh</c>) with <see cref="DateTimeKind.Unspecified"/>
    /// for comparison/persistence against PostgreSQL <c>timestamp without time zone</c> hospital-local columns.
    /// </summary>
    public static DateTime GetNow(TimeProvider timeProvider)
        => DateTime.SpecifyKind(GetNowOffset(timeProvider).DateTime, DateTimeKind.Unspecified);

    /// <summary>
    /// Returns current hospital calendar date in <c>Asia/Ho_Chi_Minh</c>.
    /// </summary>
    public static DateOnly GetToday(TimeProvider timeProvider)
        => DateOnly.FromDateTime(GetNowOffset(timeProvider).DateTime);

    /// <summary>
    /// Converts a known UTC timestamp (e.g. <c>audit_logs.occurred_at</c>) to hospital local wall-clock time.
    /// </summary>
    public static DateTime ConvertUtcToHospitalLocal(DateTime utcTimestamp)
    {
        var utc = utcTimestamp.Kind == DateTimeKind.Utc
            ? utcTimestamp
            : DateTime.SpecifyKind(utcTimestamp, DateTimeKind.Utc);

        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZone);
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// Converts a hospital-local wall-clock timestamp (<c>Asia/Ho_Chi_Minh</c>) to UTC.
    /// </summary>
    public static DateTime ConvertHospitalLocalToUtc(DateTime hospitalLocalTimestamp)
    {
        if (hospitalLocalTimestamp.Kind == DateTimeKind.Utc)
        {
            return hospitalLocalTimestamp;
        }

        var unspecified = DateTime.SpecifyKind(hospitalLocalTimestamp, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZone);
    }

    /// <summary>
    /// Normalizes a timestamp from hospital-local DB columns (e.g. <c>visits.checked_in_at</c>,
    /// <c>examination_slots.starts_at</c>): converts only if tagged <see cref="DateTimeKind.Utc"/>,
    /// avoiding +7h double-application on <see cref="DateTimeKind.Unspecified"/> hospital-local rows.
    /// </summary>
    public static DateTime EnsureHospitalLocal(DateTime timestamp)
        => timestamp.Kind == DateTimeKind.Utc
            ? ConvertUtcToHospitalLocal(timestamp)
            : DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified);

    private static TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(IanaTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fall through to Windows timezone ID or custom fixed offset.
        }
        catch (InvalidTimeZoneException)
        {
            // Fall through to Windows timezone ID or custom fixed offset.
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsFallbackTimeZoneId);
        }
        catch
        {
            return TimeZoneInfo.CreateCustomTimeZone(
                id: IanaTimeZoneId,
                baseUtcOffset: VietnamUtcOffset,
                displayName: "(UTC+07:00) Ho Chi Minh City",
                standardDisplayName: "Indochina Time");
        }
    }
}
