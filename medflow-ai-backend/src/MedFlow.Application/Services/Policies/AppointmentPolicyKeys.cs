namespace MedFlow.Application.Services.Policies;

internal static class AppointmentPolicyKeys
{
    public const string PolicyType = "APPOINTMENT";

    public const string BookingWindowDays = "booking_window_days";
    public const string BookingLeadHours = "booking_lead_hours";
    public const string MaxActiveBookings = "max_active_bookings";
    public const string RescheduleHours = "reschedule_hours";
    public const string MaxReschedules = "max_reschedules";
    public const string CancelHours = "cancel_hours";
    public const string NoShowGraceMinutes = "no_show_grace_minutes";
    public const string NoShowLimit = "no_show_limit";

    public const string PropMaxDaysAhead = "max_days_ahead";
    public const string PropMinimumHours = "minimum_hours";
    public const string PropCount = "count";
    public const string PropMinutes = "minutes";
    public const string PropMaxCount = "max_count";
    public const string PropLookbackDays = "lookback_days";
    public const string PropBlockDays = "block_days";

    public const int DefaultBookingWindowDays = 30;
    public const int DefaultBookingLeadHours = 2;
    public const int DefaultMaxActiveBookings = 3;
    public const int DefaultRescheduleMinimumHours = 12;
    public const int DefaultMaxReschedules = 2;
    public const int DefaultCancelMinimumHours = 6;
    public const int DefaultNoShowGraceMinutes = 15;
    public const int DefaultNoShowMaxCount = 3;
    public const int DefaultNoShowLookbackDays = 90;
    public const int DefaultNoShowBlockDays = 30;
}
