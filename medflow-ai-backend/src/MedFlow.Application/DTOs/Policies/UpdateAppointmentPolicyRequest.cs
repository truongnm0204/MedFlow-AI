namespace MedFlow.Application.DTOs.Policies;

public sealed class UpdateAppointmentPolicyRequest
{
    public int? BookingWindowDays { get; set; }
    public int? BookingLeadHours { get; set; }
    public int? MaxActiveBookings { get; set; }
    public int? RescheduleMinimumHours { get; set; }
    public int? MaxReschedules { get; set; }
    public int? CancelMinimumHours { get; set; }
    public int? NoShowGraceMinutes { get; set; }
    public int? NoShowMaxCount { get; set; }
    public int? NoShowLookbackDays { get; set; }
    public int? NoShowBlockDays { get; set; }
}
