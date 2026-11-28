namespace MedFlow.Domain.Entities;

public class Notification
{
    public const string ChannelEmail = "EMAIL";
    public const string ChannelInApp = "IN_APP";

    public const string StatusPending = "PENDING";
    public const string StatusProcessing = "PROCESSING";
    public const string StatusSent = "SENT";
    public const string StatusDelivered = "DELIVERED";
    public const string StatusFailed = "FAILED";
    public const string StatusCancelled = "CANCELLED";

    public const string TypeAppointmentBooked = "APPOINTMENT_BOOKED";
    public const string TypeAppointmentRescheduled = "APPOINTMENT_RESCHEDULED";
    public const string TypeAppointmentCancelled = "APPOINTMENT_CANCELLED";
    public const string TypeAppointmentReminder = "APPOINTMENT_REMINDER";
    public const string TypeAppointmentNoShow = "APPOINTMENT_NO_SHOW";
    public const string TypeVisitReturnReady = "VISIT_RETURN_READY";
    public const string TypeDiagnosticOrderDeclined = "DIAGNOSTIC_ORDER_DECLINED";

    public long Id { get; set; }
    public long? PatientId { get; set; }
    public long? RecipientUserId { get; set; }
    public long? AppointmentId { get; set; }
    public long? VisitId { get; set; }
    public string? Channel { get; set; }
    public string? Recipient { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public string? Status { get; set; }
    public int RetryCount { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? NotificationType { get; set; }
    public string? ProcessingToken { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? CreatedAt { get; set; }

    public Patient? Patient { get; set; }
    public Appointment? Appointment { get; set; }
    public Visit? Visit { get; set; }
}
