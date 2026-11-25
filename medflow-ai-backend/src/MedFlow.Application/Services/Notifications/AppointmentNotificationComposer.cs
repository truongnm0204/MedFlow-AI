using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Notifications;

public static class AppointmentNotificationComposer
{
    public const string QueueCancelledAuditAction = "QUEUE_ENTRY_CANCELLED";

    public static string FormatDoctorDisplayName(string? fullName, string? title)
        => DoctorCatalogService.FormatDisplayName(fullName, string.IsNullOrWhiteSpace(title) ? "BS" : title.Trim());

    public static Dictionary<string, HospitalPolicy> SelectCurrentPolicies(
        IEnumerable<HospitalPolicy> rows,
        DateTime nowUtc)
        => HospitalPolicyStore.SelectCurrent(rows, nowUtc);

    public static int ReadPolicyInt(HospitalPolicy? row, string propertyName, int defaultValue)
        => HospitalPolicyStore.ReadInt(row, propertyName, defaultValue);

    public static string BuildBookedIdempotencyKey(long appointmentId)
        => $"appt-booked:{appointmentId.ToString(CultureInfo.InvariantCulture)}";

    public static string BuildRescheduledIdempotencyKey(long newAppointmentId)
        => $"appt-rescheduled:{newAppointmentId.ToString(CultureInfo.InvariantCulture)}";

    public static string BuildCancelledIdempotencyKey(long appointmentId)
        => $"appt-cancelled:{appointmentId.ToString(CultureInfo.InvariantCulture)}";

    public static string BuildReminderIdempotencyKey(long appointmentId, long slotId, DateTime startsAt)
        => $"appt-reminder:{appointmentId.ToString(CultureInfo.InvariantCulture)}:{slotId.ToString(CultureInfo.InvariantCulture)}:{startsAt.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}";

    public static string BuildNoShowIdempotencyKey(long appointmentId, long slotId)
        => $"appt-noshow:{appointmentId.ToString(CultureInfo.InvariantCulture)}:{slotId.ToString(CultureInfo.InvariantCulture)}";

    public static bool IsSyntacticallyValidEmail(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim();
        if (trimmed.Length > 254 || trimmed.Contains(' '))
        {
            return false;
        }

        if (!MailAddress.TryCreate(trimmed, out var parsed))
        {
            return false;
        }

        var atIndex = parsed.Address.IndexOf('@');
        return atIndex > 0 && atIndex < parsed.Address.Length - 2 && parsed.Address[(atIndex + 1)..].Contains('.');
    }

    public static async Task<string?> ResolveAuthorizedRecipientEmailAsync(
        IApplicationDbContext db,
        long patientId,
        long? actorOrBookedByUserId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var patient = await db.Patients
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == patientId, ct);

        if (patient is null)
        {
            return null;
        }

        if (patient.User is not null &&
            patient.User.AccountStatus == AccountStatus.ACTIVE &&
            IsSyntacticallyValidEmail(patient.User.Email))
        {
            return patient.User.Email.Trim();
        }

        if (IsSyntacticallyValidEmail(patient.Email))
        {
            return patient.Email!.Trim();
        }

        if (!actorOrBookedByUserId.HasValue || actorOrBookedByUserId.Value <= 0)
        {
            return null;
        }

        var guardianUserId = actorOrBookedByUserId.Value;
        var guardianUser = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == guardianUserId, ct);

        if (guardianUser is null ||
            guardianUser.AccountStatus != AccountStatus.ACTIVE ||
            !IsSyntacticallyValidEmail(guardianUser.Email))
        {
            return null;
        }

        if (patient.UserId == guardianUserId)
        {
            return guardianUser.Email.Trim();
        }

        var ownerPatientId = await db.Patients
            .AsNoTracking()
            .Where(p => p.UserId == guardianUserId && p.Status.ToUpper() == "ACTIVE")
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(ct);

        if (!ownerPatientId.HasValue)
        {
            return null;
        }

        var hasVerifiedLink = await db.PatientFamilyLinks
            .AsNoTracking()
            .WhereActiveMemberOf(ownerPatientId.Value, nowLocal)
            .AnyAsync(l => l.MemberPatientId == patientId, ct);

        return hasVerifiedLink ? guardianUser.Email.Trim() : null;
    }

    public static Notification CreateBookedNotification(
        long patientId,
        long appointmentId,
        string appointmentCode,
        string serviceName,
        DateTime startsAt,
        DateTime endsAt,
        string? roomLabel,
        string? authorizedEmail,
        DateTime nowLocal)
    {
        var dateLabel = startsAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var timeRange = $"{startsAt.ToString("HH:mm", CultureInfo.InvariantCulture)} - {endsAt.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        var roomSuffix = string.IsNullOrWhiteSpace(roomLabel) ? string.Empty : $" tại {roomLabel.Trim()}";
        var subject = $"Xác nhận đặt lịch khám #{appointmentCode}";
        var body = $"Lịch khám {serviceName} mã {appointmentCode} vào lúc {timeRange} ngày {dateLabel}{roomSuffix} đã được xác nhận. Vui lòng có mặt trước giờ hẹn 15 phút để làm thủ tục tiếp đón.";

        return BuildEntity(
            patientId: patientId,
            appointmentId: appointmentId,
            notificationType: Notification.TypeAppointmentBooked,
            idempotencyKey: BuildBookedIdempotencyKey(appointmentId),
            subject: subject,
            body: body,
            authorizedEmail: authorizedEmail,
            nowLocal: nowLocal);
    }

    public static Notification CreateRescheduledNotification(
        long patientId,
        long newAppointmentId,
        string oldAppointmentCode,
        string newAppointmentCode,
        string serviceName,
        DateTime newStartsAt,
        DateTime newEndsAt,
        string? roomLabel,
        string? authorizedEmail,
        DateTime nowLocal)
    {
        var dateLabel = newStartsAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var timeRange = $"{newStartsAt.ToString("HH:mm", CultureInfo.InvariantCulture)} - {newEndsAt.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        var roomSuffix = string.IsNullOrWhiteSpace(roomLabel) ? string.Empty : $" tại {roomLabel.Trim()}";
        var subject = $"Đổi lịch khám thành công sang mã #{newAppointmentCode}";
        var body = $"Lịch hẹn cũ #{oldAppointmentCode} đã được chuyển sang mã mới #{newAppointmentCode} ({serviceName}) vào lúc {timeRange} ngày {dateLabel}{roomSuffix}.";

        return BuildEntity(
            patientId: patientId,
            appointmentId: newAppointmentId,
            notificationType: Notification.TypeAppointmentRescheduled,
            idempotencyKey: BuildRescheduledIdempotencyKey(newAppointmentId),
            subject: subject,
            body: body,
            authorizedEmail: authorizedEmail,
            nowLocal: nowLocal);
    }

    public static Notification CreateCancelledNotification(
        long patientId,
        long appointmentId,
        string appointmentCode,
        string serviceName,
        DateTime startsAt,
        string? authorizedEmail,
        DateTime nowLocal)
    {
        var dateLabel = startsAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var timeLabel = startsAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        var subject = $"Xác nhận hủy lịch khám #{appointmentCode}";
        var body = $"Lịch khám {serviceName} mã {appointmentCode} dự kiến lúc {timeLabel} ngày {dateLabel} đã được hủy theo yêu cầu và giải phóng khung giờ khám.";

        return BuildEntity(
            patientId: patientId,
            appointmentId: appointmentId,
            notificationType: Notification.TypeAppointmentCancelled,
            idempotencyKey: BuildCancelledIdempotencyKey(appointmentId),
            subject: subject,
            body: body,
            authorizedEmail: authorizedEmail,
            nowLocal: nowLocal);
    }

    public static Notification CreateReminderNotification(
        long patientId,
        long appointmentId,
        long slotId,
        string appointmentCode,
        string serviceName,
        string? doctorDisplayName,
        string? roomLabel,
        DateTime startsAt,
        string? authorizedEmail,
        DateTime nowLocal)
    {
        var hoursRemaining = Math.Max(1, (int)Math.Ceiling((startsAt - nowLocal).TotalHours));
        var dateLabel = startsAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var timeLabel = startsAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        var doctorClause = string.IsNullOrWhiteSpace(doctorDisplayName) ? string.Empty : $" với {doctorDisplayName.Trim()}";
        var roomClause = string.IsNullOrWhiteSpace(roomLabel) ? string.Empty : $" tại {roomLabel.Trim()}";

        var subject = $"Nhắc lịch khám sắp diễn ra #{appointmentCode} (còn khoảng {hoursRemaining} giờ)";
        var body = $"Bạn có lịch khám {serviceName}{doctorClause} vào lúc {timeLabel} ngày {dateLabel}{roomClause} (Mã lịch: {appointmentCode}). Vui lòng mang theo CCCD/BHYT và đến quầy tiếp đón trước 15 phút.";

        return BuildEntity(
            patientId: patientId,
            appointmentId: appointmentId,
            notificationType: Notification.TypeAppointmentReminder,
            idempotencyKey: BuildReminderIdempotencyKey(appointmentId, slotId, startsAt),
            subject: subject,
            body: body,
            authorizedEmail: authorizedEmail,
            nowLocal: nowLocal);
    }

    public static Notification CreateNoShowNotification(
        long patientId,
        long appointmentId,
        long slotId,
        string appointmentCode,
        string serviceName,
        DateTime startsAt,
        string? authorizedEmail,
        DateTime nowLocal)
    {
        var dateLabel = startsAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var timeLabel = startsAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        var subject = $"Ghi nhận vắng mặt lịch khám #{appointmentCode}";
        var body = $"Lịch khám {serviceName} mã {appointmentCode} lúc {timeLabel} ngày {dateLabel} đã quá thời gian tiếp đón mà chưa ghi nhận thủ tục check-in (NO_SHOW). Bạn có thể đặt lại lịch khám mới trên cổng bệnh nhân khi cần.";

        return BuildEntity(
            patientId: patientId,
            appointmentId: appointmentId,
            notificationType: Notification.TypeAppointmentNoShow,
            idempotencyKey: BuildNoShowIdempotencyKey(appointmentId, slotId),
            subject: subject,
            body: body,
            authorizedEmail: authorizedEmail,
            nowLocal: nowLocal);
    }

    public static async Task<int> InvalidatePendingRemindersAsync(
        IApplicationDbContext db,
        long appointmentId,
        string cancellationReason,
        CancellationToken ct)
    {
        var pendingReminders = await db.Notifications
            .Where(n =>
                n.AppointmentId == appointmentId &&
                n.NotificationType == Notification.TypeAppointmentReminder &&
                n.Status == Notification.StatusPending)
            .ToListAsync(ct);

        foreach (var reminder in pendingReminders)
        {
            reminder.Status = Notification.StatusCancelled;
            reminder.ProcessingToken = null;
            reminder.LeaseUntil = null;
            reminder.LastError = cancellationReason;
        }

        return pendingReminders.Count;
    }

    public static async Task<IReadOnlyList<QueueEntry>> CancelReservedQueueEntriesForAppointmentAsync(
        IApplicationDbContext db,
        long appointmentId,
        long? actorUserId,
        long? hospitalId,
        string reason,
        string? ipAddress,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var reservedEntries = await db.QueueEntries
            .Where(q =>
                q.AppointmentId == appointmentId &&
                q.VisitId == null &&
                q.Status != null &&
                q.Status.ToUpper() == "RESERVED")
            .ToListAsync(ct);

        if (reservedEntries.Count == 0)
        {
            return [];
        }

        var parsedIp = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var ip)
            ? ip
            : null;

        foreach (var entry in reservedEntries)
        {
            var previousStatus = entry.Status ?? "RESERVED";
            var previousPresence = string.IsNullOrWhiteSpace(entry.PresenceStatus)
                ? "NOT_ARRIVED"
                : entry.PresenceStatus.Trim().ToUpperInvariant();

            entry.Status = "CANCELLED";
            entry.PresenceStatus = previousPresence == "NOT_ARRIVED" ? "NOT_ARRIVED" : "LEFT";
            entry.StateVersion = (entry.StateVersion ?? 1) + 1;
            entry.BlockedReason = reason;

            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = hospitalId > 0 ? hospitalId : null,
                Action = QueueCancelledAuditAction,
                EntityType = "QUEUE_ENTRY",
                EntityId = entry.Id.ToString(CultureInfo.InvariantCulture),
                Reason = reason,
                ChangeData = JsonSerializer.Serialize(new
                {
                    queueEntryId = entry.Id,
                    appointmentId,
                    queueNumber = entry.QueueNumber,
                    previousStatus,
                    currentStatus = entry.Status,
                    presenceStatus = entry.PresenceStatus,
                    stateVersion = entry.StateVersion,
                    reason,
                }),
                IpAddress = parsedIp,
                OccurredAt = nowUtc,
            });
        }

        return reservedEntries;
    }

    public static string BuildEmailHtmlBody(string subject, string messageBody)
    {
        var encodedTitle = WebUtility.HtmlEncode(subject);
        var encodedMessage = WebUtility.HtmlEncode(messageBody);
        return $"""
            <div style="font-family: Inter, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 12px; color: #0f172a;">
              <h2 style="margin: 0 0 12px; font-size: 18px; color: #0284c7;">MedFlow AI — Thông báo Lịch khám</h2>
              <p style="margin: 0 0 8px; font-weight: 600; font-size: 15px;">{encodedTitle}</p>
              <p style="margin: 0 0 16px; font-size: 14px; line-height: 1.6; color: #334155;">{encodedMessage}</p>
              <hr style="border: none; border-top: 1px solid #e2e8f0; margin: 16px 0;" />
              <p style="margin: 0; font-size: 12px; color: #64748b;">Đây là email tự động từ Hệ thống Quản lý Khám ngoại trú MedFlow AI. Vui lòng không trả lời trực tiếp thư này.</p>
            </div>
            """;
    }

    private static Notification BuildEntity(
        long patientId,
        long appointmentId,
        string notificationType,
        string idempotencyKey,
        string subject,
        string body,
        string? authorizedEmail,
        DateTime nowLocal)
    {
        var hasValidEmail = IsSyntacticallyValidEmail(authorizedEmail);

        return new Notification
        {
            PatientId = patientId,
            AppointmentId = appointmentId,
            VisitId = null,
            Channel = hasValidEmail ? Notification.ChannelEmail : Notification.ChannelInApp,
            Recipient = hasValidEmail ? authorizedEmail!.Trim() : null,
            Subject = subject.Length > 255 ? subject[..255] : subject,
            Body = body,
            Status = hasValidEmail ? Notification.StatusPending : Notification.StatusDelivered,
            RetryCount = 0,
            IdempotencyKey = idempotencyKey,
            NotificationType = notificationType,
            ProcessingToken = null,
            LeaseUntil = null,
            ScheduledAt = nowLocal,
            SentAt = null,
            ReadAt = null,
            LastError = null,
            CreatedAt = nowLocal,
        };
    }
}
