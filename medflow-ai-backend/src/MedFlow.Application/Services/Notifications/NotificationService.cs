using System.Globalization;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Notifications;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Notifications;

public sealed class NotificationService : INotificationService
{
    private const int DefaultLimit = 30;
    private const int MaxLimit = 100;
    private const int DefaultReminderHours = 24;
    private const string NotificationPolicyType = "NOTIFICATION";
    private const string ReminderHoursPolicyKey = "reminder_hours";

    private static readonly IReadOnlyList<string> StandardPreparationNotes =
    [
        "Có mặt tại quầy tiếp đón trước giờ hẹn tối thiểu 15 phút để đối chiếu thông tin và kích hoạt số thứ tự khám.",
        "Nhịn ăn sáng từ 6 đến 8 giờ nếu dự kiến thực hiện xét nghiệm máu hoặc siêu âm ổ bụng.",
        "Mang theo danh sách thuốc đang sử dụng và các kết quả khám, xét nghiệm gần nhất (nếu có).",
    ];

    private static readonly IReadOnlyList<string> StandardDocumentsRequired =
    [
        "Căn cước công dân gắn chip hoặc ứng dụng VNeID mức độ 2.",
        "Thẻ Bảo hiểm y tế hoặc hình ảnh thẻ trên ứng dụng VssID (nếu có).",
        "Sổ khám bệnh, đơn thuốc cũ hoặc kết quả cận lâm sàng trong 3 tháng gần nhất (nếu có).",
    ];

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public NotificationService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<NotificationFeedResponse> GetMyNotificationsAsync(
        long userId,
        GetNotificationsRequest? request,
        CancellationToken ct = default)
    {
        if (userId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var limit = request is null ? DefaultLimit : request.Limit;
        if (limit < 1 || limit > MaxLimit)
        {
            throw new ValidationException($"Số lượng thông báo mỗi lần tải phải từ 1 đến {MaxLimit}.");
        }

        var unreadOnly = request?.UnreadOnly == true;
        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var ownedPatientIds = await LoadOwnedPatientIdsAsync(userId, nowLocal, ct);

        var visibleQuery = _db.Notifications
            .AsNoTracking()
            .Where(n =>
                ((n.PatientId != null && ownedPatientIds.Contains(n.PatientId.Value)) ||
                 n.RecipientUserId == userId) &&
                (n.Status == null || n.Status != Notification.StatusCancelled));

        var unreadCount = await visibleQuery.CountAsync(n => n.ReadAt == null, ct);

        var listQuery = unreadOnly
            ? visibleQuery.Where(n => n.ReadAt == null)
            : visibleQuery;

        var rows = await listQuery
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(limit)
            .ToListAsync(ct);

        var referencedAppointmentIds = rows
            .Where(r => r.AppointmentId.HasValue)
            .Select(r => r.AppointmentId!.Value)
            .Distinct()
            .ToList();

        var appointmentsById = referencedAppointmentIds.Count == 0
            ? new Dictionary<long, Appointment>()
            : await _db.Appointments
                .AsNoTracking()
                .IncludeDisplayGraph()
                .Where(a => referencedAppointmentIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, ct);

        var items = rows.Select(row =>
        {
            AppointmentReminderDto? reminderDto = null;
            if (row.AppointmentId.HasValue &&
                appointmentsById.TryGetValue(row.AppointmentId.Value, out var linkedAppt) &&
                linkedAppt.Slot?.StartsAt is not null &&
                ShouldAttachReminderModal(row.NotificationType, linkedAppt))
            {
                reminderDto = MapReminderDto(linkedAppt, row.CreatedAt ?? nowLocal, nowLocal, today);
            }

            return MapNotificationDto(row, reminderDto, nowLocal, today);
        }).ToList();

        // ActiveReminder is derived strictly from active Appointment state within the hospital's reminder window,
        // completely independent of Notification.ReadAt so reading bell alerts never hides an active reminder banner.
        var activeReminder = await ResolveActiveReminderAsync(ownedPatientIds, nowUtc, nowLocal, today, ct);

        return new NotificationFeedResponse(
            UnreadCount: unreadCount,
            ActiveReminder: activeReminder,
            Items: items);
    }

    public async Task<MarkNotificationReadResponse> MarkAsReadAsync(
        long userId,
        long notificationId,
        CancellationToken ct = default)
    {
        if (userId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        if (notificationId <= 0)
        {
            throw new ValidationException("Mã thông báo không hợp lệ.");
        }

        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var ownedPatientIds = await LoadOwnedPatientIdsAsync(userId, nowLocal, ct);

        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId, ct)
            ?? throw new NotFoundException("Notification", notificationId);

        if ((!notification.PatientId.HasValue || !ownedPatientIds.Contains(notification.PatientId.Value)) &&
            notification.RecipientUserId != userId)
        {
            throw new ForbiddenException("Bạn không có quyền truy cập hoặc cập nhật thông báo này.");
        }

        if (notification.ReadAt is null)
        {
            notification.ReadAt = nowLocal;
            await _db.SaveChangesAsync(ct);
        }

        var unreadCount = await CountUnreadAsync(ownedPatientIds, userId, ct);
        var readAtIso = AppointmentReadModel.FormatVietnamTimestamp(notification.ReadAt.Value);

        return new MarkNotificationReadResponse(
            Id: notification.Id.ToString(CultureInfo.InvariantCulture),
            IsRead: true,
            ReadAtIso: readAtIso,
            UnreadCount: unreadCount);
    }

    public async Task<MarkAllNotificationsReadResponse> MarkAllAsReadAsync(
        long userId,
        CancellationToken ct = default)
    {
        if (userId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var ownedPatientIds = await LoadOwnedPatientIdsAsync(userId, nowLocal, ct);

        var unreadRows = await _db.Notifications
            .Where(n =>
                ((n.PatientId != null && ownedPatientIds.Contains(n.PatientId.Value)) ||
                 n.RecipientUserId == userId) &&
                n.ReadAt == null &&
                (n.Status == null || n.Status != Notification.StatusCancelled))
            .ToListAsync(ct);

        foreach (var row in unreadRows)
        {
            row.ReadAt = nowLocal;
        }

        if (unreadRows.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return new MarkAllNotificationsReadResponse(
            MarkedCount: unreadRows.Count,
            UnreadCount: 0);
    }

    private async Task<HashSet<long>> LoadOwnedPatientIdsAsync(
        long userId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        try
        {
            var owned = await AppointmentBookingRules.LoadOwnedPatientsAsync(_db, userId, nowLocal, ct);
            var set = new HashSet<long> { owned.Self.Id };
            foreach (var link in owned.FamilyLinks)
            {
                set.Add(link.MemberPatientId);
            }

            return set;
        }
        catch (Exception ex) when (ex is NotFoundException or ForbiddenException)
        {
            // Bác sĩ/nhân viên nhận thông báo qua recipient_user_id, không có hồ sơ bệnh nhân sở hữu.
            return [];
        }
    }

    private Task<int> CountUnreadAsync(
        HashSet<long> ownedPatientIds,
        long userId,
        CancellationToken ct)
        => _db.Notifications
            .AsNoTracking()
            .CountAsync(
                n => ((n.PatientId != null && ownedPatientIds.Contains(n.PatientId.Value)) ||
                      n.RecipientUserId == userId) &&
                     n.ReadAt == null &&
                     (n.Status == null || n.Status != Notification.StatusCancelled),
                ct);

    private async Task<AppointmentReminderDto?> ResolveActiveReminderAsync(
        HashSet<long> ownedPatientIds,
        DateTime nowUtc,
        DateTime nowLocal,
        DateOnly today,
        CancellationToken ct)
    {
        var upcomingCandidates = await _db.Appointments
            .AsNoTracking()
            .IncludeDisplayGraph()
            .Where(a =>
                a.PatientId != null &&
                ownedPatientIds.Contains(a.PatientId.Value) &&
                (a.Status == "BOOKED" || a.Status == "CONFIRMED") &&
                a.Slot != null &&
                a.Slot.StartsAt != null &&
                a.Slot.StartsAt > nowLocal &&
                a.Slot.StartsAt <= nowLocal.AddHours(168))
            .OrderBy(a => a.Slot!.StartsAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct);

        if (upcomingCandidates.Count == 0)
        {
            return null;
        }

        var candidateIds = upcomingCandidates.Select(a => a.Id).ToList();
        var attendedApptIds = (await _db.Visits
            .AsNoTracking()
            .Where(v =>
                v.AppointmentId != null &&
                candidateIds.Contains(v.AppointmentId.Value) &&
                (v.Status == null || v.Status.ToUpper() != AppointmentBookingRules.CancelledStatus))
            .Select(v => v.AppointmentId!.Value)
            .ToListAsync(ct))
            .ToHashSet();

        var reminderHoursCache = new Dictionary<long, int>();

        foreach (var appt in upcomingCandidates)
        {
            if (attendedApptIds.Contains(appt.Id))
            {
                continue;
            }

            var slot = appt.Slot!;
            var startsAt = slot.StartsAt!.Value;
            var hospitalId = slot.Service?.HospitalId
                ?? slot.Room?.HospitalId
                ?? slot.Doctor?.HospitalId
                ?? 0L;

            if (!reminderHoursCache.TryGetValue(hospitalId, out var reminderHours))
            {
                reminderHours = await ReadHospitalReminderHoursAsync(hospitalId, nowUtc, ct);
                reminderHoursCache[hospitalId] = reminderHours;
            }

            if (startsAt <= nowLocal.AddHours(reminderHours))
            {
                var notifiedTimestamp = appt.BookedAt ?? nowLocal;
                return MapReminderDto(appt, notifiedTimestamp, nowLocal, today);
            }
        }

        return null;
    }

    private async Task<int> ReadHospitalReminderHoursAsync(
        long hospitalId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (hospitalId <= 0)
        {
            return DefaultReminderHours;
        }

        var row = await _db.HospitalPolicies
            .AsNoTracking()
            .Where(p =>
                p.HospitalId == hospitalId &&
                p.PolicyType == NotificationPolicyType &&
                p.PolicyKey == ReminderHoursPolicyKey &&
                p.QueueServicePointId == null &&
                (p.EffectiveFrom == null || p.EffectiveFrom <= nowUtc) &&
                (p.EffectiveTo == null || p.EffectiveTo > nowUtc))
            .OrderByDescending(p => p.PolicyVersion ?? 0)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);

        if (row is null || string.IsNullOrWhiteSpace(row.PolicyValue))
        {
            return DefaultReminderHours;
        }

        try
        {
            using var doc = JsonDocument.Parse(row.PolicyValue);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("hours_before", out var prop) &&
                prop.TryGetInt32(out var hours) &&
                hours > 0)
            {
                return Math.Clamp(hours, 1, 168);
            }
        }
        catch (JsonException)
        {
            // Fallback to default reminder window.
        }

        return DefaultReminderHours;
    }

    private static bool ShouldAttachReminderModal(string? notificationType, Appointment appt)
    {
        var statusKey = AppointmentBookingRules.NormalizeStatusKey(appt.Status);
        if (statusKey is "CANCELLED" or "NO_SHOW")
        {
            return false;
        }

        var normalizedType = notificationType?.Trim().ToUpperInvariant();
        return normalizedType is Notification.TypeAppointmentReminder
            or Notification.TypeAppointmentBooked
            or Notification.TypeAppointmentRescheduled
            or null;
    }

    private static PatientNotificationDto MapNotificationDto(
        Notification row,
        AppointmentReminderDto? reminder,
        DateTime nowLocal,
        DateOnly today)
    {
        var type = string.IsNullOrWhiteSpace(row.NotificationType)
            ? Notification.TypeAppointmentBooked
            : row.NotificationType.Trim().ToUpperInvariant();

        var category = type switch
        {
            Notification.TypeAppointmentReminder => "APPOINTMENT_REMINDER",
            Notification.TypeAppointmentBooked => "APPOINTMENT_REMINDER",
            Notification.TypeAppointmentRescheduled => "APPOINTMENT_REMINDER",
            Notification.TypeAppointmentCancelled => "SYSTEM",
            Notification.TypeAppointmentNoShow => "SYSTEM",
            _ => "SYSTEM",
        };

        var createdLocal = row.CreatedAt ?? row.ScheduledAt ?? nowLocal;

        return new PatientNotificationDto(
            Id: row.Id.ToString(CultureInfo.InvariantCulture),
            Title: string.IsNullOrWhiteSpace(row.Subject) ? "Thông báo từ MedFlow AI" : row.Subject.Trim(),
            Message: row.Body?.Trim() ?? string.Empty,
            Category: category,
            NotificationType: type,
            CreatedAt: FormatRelativeTimeLabel(createdLocal, nowLocal, today),
            CreatedAtIso: AppointmentReadModel.FormatVietnamTimestamp(createdLocal),
            IsRead: row.ReadAt.HasValue,
            AppointmentReminder: reminder);
    }

    private static AppointmentReminderDto MapReminderDto(
        Appointment appt,
        DateTime notifiedAtLocal,
        DateTime nowLocal,
        DateOnly today)
    {
        var slot = appt.Slot!;
        var startsAt = slot.StartsAt!.Value;
        var service = slot.Service;
        var doctor = slot.Doctor;
        var room = slot.Room;

        var appointmentIdStr = appt.Id.ToString(CultureInfo.InvariantCulture);
        var appointmentCode = !string.IsNullOrWhiteSpace(appt.AppointmentCode)
            ? appt.AppointmentCode!
            : $"APT-{appointmentIdStr}";

        var patientName = !string.IsNullOrWhiteSpace(appt.Patient?.FullName)
            ? appt.Patient!.FullName
            : "Người bệnh";

        var doctorTitle = string.IsNullOrWhiteSpace(doctor?.ProfessionalTitle) ? "BS" : doctor!.ProfessionalTitle!.Trim();
        var doctorName = doctor?.Staff?.FullName is { Length: > 0 } fullName
            ? DoctorCatalogService.FormatDisplayName(fullName, doctorTitle)
            : "Bác sĩ chuyên khoa trực ca";

        var specialtyName = !string.IsNullOrWhiteSpace(service?.Specialty?.Name)
            ? service!.Specialty!.Name
            : (!string.IsNullOrWhiteSpace(doctor?.Specialty?.Name)
                ? doctor!.Specialty!.Name
                : (!string.IsNullOrWhiteSpace(service?.Name) ? service!.Name : "Khám ngoại trú"));

        string roomName;
        if (room is not null)
        {
            var baseRoom = !string.IsNullOrWhiteSpace(room.Name) ? room.Name.Trim() : room.RoomCode.Trim();
            roomName = !string.IsNullOrWhiteSpace(room.Location)
                ? $"{baseRoom} • {room.Location.Trim()}"
                : baseRoom;
        }
        else
        {
            roomName = "Quầy tiếp đón phân buồng khám";
        }

        var rawHours = (startsAt - nowLocal).TotalHours;
        var hoursRemaining = rawHours <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(rawHours));
        var urgency = rawHours <= 4.0
            ? "URGENT"
            : (rawHours <= 24.0 ? "UPCOMING" : "NORMAL");

        var isConfirmed = string.Equals(appt.Status?.Trim(), "CONFIRMED", StringComparison.OrdinalIgnoreCase);

        return new AppointmentReminderDto(
            Id: $"rem-{appointmentIdStr}",
            AppointmentId: appointmentIdStr,
            AppointmentCode: appointmentCode,
            PatientName: patientName,
            DoctorName: doctorName,
            DoctorAvatar: doctor?.ProfileImageUrl,
            SpecialtyName: specialtyName,
            RoomName: roomName,
            ScheduledDate: startsAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ScheduledTime: startsAt.ToString("HH:mm", CultureInfo.InvariantCulture),
            hoursRemaining,
            Urgency: urgency,
            PreparationNotes: StandardPreparationNotes,
            DocumentsRequired: StandardDocumentsRequired,
            IsConfirmedByPatient: isConfirmed,
            NotifiedAt: FormatRelativeTimeLabel(notifiedAtLocal, nowLocal, today));
    }

    private static string FormatRelativeTimeLabel(DateTime timestampLocal, DateTime nowLocal, DateOnly today)
    {
        var diff = nowLocal - timestampLocal;
        if (diff.TotalMinutes < 1 && diff.TotalMinutes >= -5)
        {
            return "Vừa xong";
        }

        if (diff.TotalMinutes is >= 1 and < 60)
        {
            return $"{(int)Math.Floor(diff.TotalMinutes)} phút trước";
        }

        var dateOnly = DateOnly.FromDateTime(timestampLocal);
        var timePart = timestampLocal.ToString("HH:mm", CultureInfo.InvariantCulture);

        if (dateOnly == today)
        {
            return $"Hôm nay, {timePart}";
        }

        if (dateOnly == today.AddDays(-1))
        {
            return $"Hôm qua, {timePart}";
        }

        return timestampLocal.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }
}
