namespace MedFlow.Application.DTOs.Notifications;

public sealed record GetNotificationsRequest
{
    public int Limit { get; init; } = 30;
    public bool UnreadOnly { get; init; }
}

public sealed record AppointmentReminderDto(
    string Id,
    string AppointmentId,
    string AppointmentCode,
    string PatientName,
    string DoctorName,
    string? DoctorAvatar,
    string SpecialtyName,
    string RoomName,
    string ScheduledDate,
    string ScheduledTime,
    int HoursRemaining,
    string Urgency,
    IReadOnlyList<string> PreparationNotes,
    IReadOnlyList<string> DocumentsRequired,
    bool IsConfirmedByPatient,
    string NotifiedAt);

public sealed record PatientNotificationDto(
    string Id,
    string Title,
    string Message,
    string Category,
    string NotificationType,
    string CreatedAt,
    string CreatedAtIso,
    bool IsRead,
    AppointmentReminderDto? AppointmentReminder);

public sealed record NotificationFeedResponse(
    int UnreadCount,
    AppointmentReminderDto? ActiveReminder,
    IReadOnlyList<PatientNotificationDto> Items);

public sealed record MarkNotificationReadResponse(
    string Id,
    bool IsRead,
    string ReadAtIso,
    int UnreadCount);

public sealed record MarkAllNotificationsReadResponse(
    int MarkedCount,
    int UnreadCount);
