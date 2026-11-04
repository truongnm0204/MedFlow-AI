using System.Globalization;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

public static class AppointmentBookingRules
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private const int MaxAppointmentIdentifierLength = 40;

    public const string CancelledStatus = "CANCELLED";
    public const string BookedStatus = "BOOKED";
    public const string CheckedInStatus = "CHECKED_IN";
    public const string CompletedStatus = "COMPLETED";
    public const string OnlineChannel = "ONLINE";
    public const string NoShowStatus = "NO_SHOW";

    public const string BookingActiveAppointmentExistsMessage =
        "Bệnh nhân đã có lịch khám đang hoạt động trong ngày này. Vui lòng hoàn thành hoặc hủy lịch khám hiện tại trước khi đặt lịch mới.";

    public const string BookingVisitNotCompletedMessage =
        "Bệnh nhân đang trong quá trình khám bệnh. Vui lòng hoàn tất lượt khám hiện tại trước khi đặt lịch khám mới trong cùng ngày.";

    public const string BookingSlotStartedMessage =
        "Khung giờ khám đã bắt đầu hoặc đã qua. Vui lòng chọn khung giờ khác.";

    public static readonly string[] InactiveStatuses = { "CANCELLED", "COMPLETED", "NO_SHOW" };
    public static readonly TimeSpan AfternoonShiftStart = new(12, 0, 0);

    public static string NormalizeStatusKey(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return string.Empty;

        return status.Trim()
            .ToUpperInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
    }

    public static bool IsActiveVisitStatus(string? visitStatus)
    {
        var key = NormalizeStatusKey(visitStatus);
        if (string.IsNullOrEmpty(key))
            return false;

        return key is not ("COMPLETED" or "CANCELLED");
    }

    public static bool IsCompletedVisitStatus(string? visitStatus)
        => NormalizeStatusKey(visitStatus) == CompletedStatus;
    public sealed record OwnedPatients(Patient Self, IReadOnlyList<PatientFamilyLink> FamilyLinks);

    public static DateTime GetVietnamNow(TimeProvider timeProvider)
        => DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime.Add(VietnamOffset), DateTimeKind.Unspecified);

    public static (string Trimmed, long? Id, string? Code) ParseAppointmentIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new ValidationException("Mã lịch hẹn không hợp lệ.");

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].TrimStart();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxAppointmentIdentifierLength)
            throw new ValidationException("Mã lịch hẹn không hợp lệ.");

        if (trimmed.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ValidationException("Mã lịch hẹn không hợp lệ.");

            return (trimmed, id, null);
        }

        return (trimmed, null, trimmed.ToUpperInvariant());
    }

    public static void EnsureSlotBookable(
        ExaminationSlot slot,
        DateTime nowLocal,
        DateOnly today,
        AppointmentPolicyDto policy)
    {
        if (!string.Equals(slot.Status, "OPEN", StringComparison.OrdinalIgnoreCase) ||
            slot.StartsAt is null ||
            slot.EndsAt is null)
        {
            throw new ConflictException("Khung giờ khám không còn mở để đặt lịch.");
        }
        if (slot.StartsAt.Value <= nowLocal)
            throw new ValidationException(BookingSlotStartedMessage);

        if (slot.StartsAt.Value < nowLocal.AddHours(policy.BookingLeadHours))
            throw new ValidationException($"Chỉ được đặt lịch trước giờ khám tối thiểu {policy.BookingLeadHours} giờ.");
        if (DateOnly.FromDateTime(slot.StartsAt.Value) > today.AddDays(policy.BookingWindowDays))
            throw new ValidationException($"Chỉ được đặt lịch trong vòng {policy.BookingWindowDays} ngày tới.");
    }

    public static async Task EnsureSlotHasCapacityAsync(
        IApplicationDbContext db,
        ExaminationSlot slot,
        CancellationToken ct)
    {
        var occupying = await db.Appointments
            .AsNoTracking()
            .CountAsync(a =>
                a.SlotId == slot.Id &&
                (a.Status == null || a.Status.ToUpper() != CancelledStatus), ct);

        if (occupying >= (slot.Capacity ?? 0))
            throw new ConflictException("Khung giờ khám vừa hết chỗ. Vui lòng chọn khung giờ khác.");
    }

    public static async Task EnsurePatientLimitsAsync(
        IApplicationDbContext db,
        long patientId,
        ExaminationSlot slot,
        DateTime nowLocal,
        AppointmentPolicyDto policy,
        long? excludeAppointmentId,
        CancellationToken ct)
    {
        var excludeId = excludeAppointmentId ?? 0L;

        var slotStartsAt = slot.StartsAt!.Value;
        var examDate = DateOnly.FromDateTime(slotStartsAt);
        var dayStart = examDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);

        var sameDayAppointments = await db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.Id != excludeId &&
                a.PatientId == patientId &&
                a.Slot != null &&
                a.Slot.StartsAt != null &&
                a.Slot.StartsAt >= dayStart &&
                a.Slot.StartsAt < dayEnd)
            .Select(a => new
            {
                a.Id,
                a.Status
            })
            .ToListAsync(ct);

        var sameDayApptIds = sameDayAppointments.Select(a => a.Id).ToList();

        var sameDayVisits = new List<Visit>();
        if (sameDayApptIds.Count > 0)
        {
            var byAppt = await db.Visits
                .AsNoTracking()
                .Where(v => v.AppointmentId != null && sameDayApptIds.Contains(v.AppointmentId.Value))
                .ToListAsync(ct);
            sameDayVisits.AddRange(byAppt);
        }

        var byPatientDate = await db.Visits
            .AsNoTracking()
            .Where(v =>
                v.PatientId == patientId &&
                (v.AppointmentId == null || v.AppointmentId != excludeId) &&
                v.CheckedInAt != null &&
                v.CheckedInAt >= dayStart &&
                v.CheckedInAt < dayEnd)
            .ToListAsync(ct);

        foreach (var v in byPatientDate)
        {
            if (sameDayVisits.All(existing => existing.Id != v.Id))
                sameDayVisits.Add(v);
        }
        if (sameDayVisits.Any(v => IsActiveVisitStatus(v.Status)))
            throw new ConflictException(BookingVisitNotCompletedMessage);

        foreach (var existingAppt in sameDayAppointments)
        {
            var apptStatusKey = NormalizeStatusKey(existingAppt.Status);
            if (string.IsNullOrEmpty(apptStatusKey))
                apptStatusKey = BookedStatus;

            if (apptStatusKey is CancelledStatus or NoShowStatus or CompletedStatus)
                continue;

            var linkedVisits = sameDayVisits
                .Where(v => v.AppointmentId == existingAppt.Id)
                .ToList();

            var hasCompletedVisit = linkedVisits.Any(v => IsCompletedVisitStatus(v.Status));
            var hasCancelledVisitOnly = linkedVisits.Count > 0 &&
                                        linkedVisits.All(v => NormalizeStatusKey(v.Status) == CancelledStatus);

            if (apptStatusKey == CheckedInStatus)
            {
                if (!hasCompletedVisit && !hasCancelledVisitOnly)
                    throw new ConflictException(BookingVisitNotCompletedMessage);

                continue;
            }

            if (!hasCompletedVisit)
                throw new ConflictException(BookingActiveAppointmentExistsMessage);
        }

        var candidateActiveAppointments = await db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.Id != excludeId &&
                a.PatientId == patientId &&
                a.Slot != null &&
                a.Slot.EndsAt != null &&
                a.Slot.EndsAt > nowLocal)
            .Select(a => new { a.Id, a.Status })
            .ToListAsync(ct);

        var candidateIds = candidateActiveAppointments.Select(a => a.Id).ToList();
        var terminalVisitApptIds = candidateIds.Count == 0
            ? new HashSet<long>()
            : (await db.Visits
                .AsNoTracking()
                .Where(v =>
                    v.AppointmentId != null &&
                    candidateIds.Contains(v.AppointmentId.Value) &&
                    v.Status != null &&
                    (v.Status.ToUpper() == CompletedStatus || v.Status.ToUpper() == CancelledStatus))
                .Select(v => v.AppointmentId!.Value)
                .ToListAsync(ct))
                .ToHashSet();

        var activeCount = candidateActiveAppointments.Count(a =>
        {
            var key = NormalizeStatusKey(a.Status);
            if (key is CancelledStatus or NoShowStatus or CompletedStatus)
                return false;

            return !terminalVisitApptIds.Contains(a.Id);
        });

        if (activeCount >= policy.MaxActiveBookings)
            throw new ConflictException($"Hồ sơ này đã đạt tối đa {policy.MaxActiveBookings} lịch khám đang hoạt động.");
        if (policy.NoShowBlockDays > 0)
        {
            var lookbackStart = nowLocal.AddDays(-policy.NoShowLookbackDays);
            var noShowStarts = await db.Appointments
                .AsNoTracking()
                .Where(a =>
                    a.PatientId == patientId &&
                    a.Status != null &&
                    a.Status.ToUpper() == NoShowStatus &&
                    a.Slot != null &&
                    a.Slot.StartsAt != null &&
                    a.Slot.StartsAt >= lookbackStart)
                .Select(a => a.Slot!.StartsAt!.Value)
                .ToListAsync(ct);

            if (noShowStarts.Count >= policy.NoShowMaxCount)
            {
                var latestNoShowStartsAt = noShowStarts.Max();
                if (latestNoShowStartsAt.AddDays(policy.NoShowBlockDays) > nowLocal)
                    throw new ConflictException("Hồ sơ đang bị tạm khóa đặt lịch trực tuyến do vắng mặt nhiều lần.");
            }
        }
    }

    public static async Task<long> ResolveHospitalIdAsync(
        IApplicationDbContext db,
        long serviceId,
        long? doctorId,
        CancellationToken ct)
    {
        var hospitalId = await db.Services
            .AsNoTracking()
            .Where(s => s.Id == serviceId)
            .Select(s => s.HospitalId)
            .FirstOrDefaultAsync(ct);

        if (hospitalId is null && doctorId.HasValue)
        {
            var docId = doctorId.Value;
            hospitalId = await db.Doctors
                .AsNoTracking()
                .Where(d => d.Id == docId)
                .Select(d => d.HospitalId)
                .FirstOrDefaultAsync(ct);
        }

        return hospitalId ?? 0L;
    }

    public static async Task<OwnedPatients> LoadOwnedPatientsAsync(
        IApplicationDbContext db,
        long userId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var self = await PatientAccessScope.LoadActiveSelfAsync(db, userId, ct);

        var familyLinks = await db.PatientFamilyLinks
            .AsNoTracking()
            .Include(l => l.MemberPatient)
            .WhereActiveMemberOf(self.Id, nowLocal)
            .OrderBy(l => l.MemberPatientId)
            .ToListAsync(ct);

        return new OwnedPatients(self, familyLinks);
    }

    public static async Task<string> ComputeQueueNumberAsync(
        IApplicationDbContext db,
        long appointmentId,
        ExaminationSlot slot,
        CancellationToken ct)
    {
        var dayStart = slot.StartsAt!.Value.Date;
        var dayEnd = dayStart.AddDays(1);

        var query = db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.Id <= appointmentId &&
                a.Slot != null &&
                a.Slot.StartsAt != null &&
                a.Slot.StartsAt >= dayStart &&
                a.Slot.StartsAt < dayEnd);

        if (slot.DoctorId.HasValue)
        {
            var doctorId = slot.DoctorId.Value;
            query = query.Where(a => a.Slot!.DoctorId == doctorId);
        }
        else if (slot.RoomId.HasValue)
        {
            var roomId = slot.RoomId.Value;
            query = query.Where(a => a.Slot!.RoomId == roomId);
        }
        else
        {
            var slotId = slot.Id;
            query = query.Where(a => a.SlotId == slotId);
        }

        var rank = await query.CountAsync(ct);
        return rank.ToString("D3", CultureInfo.InvariantCulture);
    }
}
