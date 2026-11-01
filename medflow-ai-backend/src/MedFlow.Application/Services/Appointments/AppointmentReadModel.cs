using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

internal static class AppointmentReadModel
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    public static readonly TimeSpan AfternoonShiftStart = new(12, 0, 0);
    public const string FeePolicyNoteText = "Giá niêm yết dịch vụ khám • Không áp dụng BHYT";

    internal sealed record ScopeMember(
        Patient Patient,
        string RelationshipLabel,
        string AgeLabel);

    internal sealed record RelatedRecords(
        IReadOnlyDictionary<long, Visit> VisitsByAppointmentId,
        IReadOnlyDictionary<long, QueueEntry> QueueByAppointmentId,
        IReadOnlyDictionary<long, Payment> PaidByVisitId);

    public static IQueryable<Appointment> IncludeDisplayGraph(this IQueryable<Appointment> query)
        => query
            .Include(a => a.Patient)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Service)
                    .ThenInclude(svc => svc!.Specialty)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Service)
                    .ThenInclude(svc => svc!.Department)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Service)
                    .ThenInclude(svc => svc!.ServicePrices)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
                    .ThenInclude(d => d!.Staff)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
                    .ThenInclude(d => d!.Specialty)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room)
                    .ThenInclude(r => r!.Department);

    public static async Task<RelatedRecords> LoadRelatedAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<long> appointmentIds,
        CancellationToken ct)
    {
        Dictionary<long, Visit> visitsByApptId = [];
        Dictionary<long, QueueEntry> queueByApptId = [];
        Dictionary<long, Payment> paidByVisitId = [];

        if (appointmentIds.Count > 0)
        {
            var visits = await db.Visits
                .AsNoTracking()
                .Where(v => v.AppointmentId != null && appointmentIds.Contains(v.AppointmentId.Value))
                .ToListAsync(ct);

            visitsByApptId = visits
                .GroupBy(v => v.AppointmentId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.Id).First());

            var queueEntries = await db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    q.AppointmentId != null &&
                    appointmentIds.Contains(q.AppointmentId.Value) &&
                    q.QueueType != null &&
                    q.QueueType.ToUpper() == "EXAMINATION")
                .ToListAsync(ct);

            queueByApptId = queueEntries
                .GroupBy(q => q.AppointmentId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(q => q.Id).First());

            if (visitsByApptId.Count > 0)
            {
                var visitIds = visitsByApptId.Values.Select(v => v.Id).ToList();

                var payments = await db.Payments
                    .AsNoTracking()
                    .Where(p =>
                        p.VisitId != null &&
                        visitIds.Contains(p.VisitId.Value) &&
                        p.Status != null &&
                        p.Status.ToUpper() == "PAID")
                    .ToListAsync(ct);

                paidByVisitId = payments
                    .GroupBy(p => p.VisitId!.Value)
                    .ToDictionary(
                        g => g.Key,
                        g => g
                            .OrderByDescending(p => p.PaidAt.HasValue)
                            .ThenByDescending(p => p.PaidAt)
                            .ThenByDescending(p => p.Id)
                            .First());
            }
        }

        return new RelatedRecords(visitsByApptId, queueByApptId, paidByVisitId);
    }

    public static List<ScopeMember> BuildScopeMembers(
        Patient self,
        IReadOnlyList<PatientFamilyLink> familyLinks,
        DateOnly today)
    {
        var list = new List<ScopeMember>(1 + familyLinks.Count)
        {
            new(self, "Bản thân", FormatAgeLabel(self.DateOfBirth, today))
        };

        var seenIds = new HashSet<long> { self.Id };
        foreach (var link in familyLinks)
        {
            var member = link.MemberPatient!;
            if (!seenIds.Add(member.Id))
                continue;

            list.Add(new ScopeMember(
                member,
                FormatRelationshipLabel(link.Relationship),
                FormatAgeLabel(member.DateOfBirth, today)));
        }

        return list;
    }

    public static string FormatRelationshipLabel(string? relationship)
    {
        var upper = relationship?.Trim().ToUpperInvariant();
        return upper switch
        {
            "CHILD" => "Con",
            "SPOUSE" => "Vợ/Chồng",
            "PARENT" => "Cha/Mẹ",
            "SIBLING" => "Anh/Chị/Em",
            _ => "Người thân"
        };
    }

    public static string FormatAgeLabel(DateOnly? dob, DateOnly today)
        => dob is null
            ? "Chưa rõ tuổi"
            : $"{AppointmentSlotService.CalculateAge(dob, today).ToString(CultureInfo.InvariantCulture)} tuổi";

    public static AppointmentListItemDto MapItem(
        Appointment a,
        ScopeMember scopeMember,
        Visit? visit,
        QueueEntry? queue,
        Payment? paid,
        DateTime nowLocal,
        DateOnly today)
    {
        var vi = CultureInfo.InvariantCulture;
        var s = a.Slot!;
        var start = s.StartsAt!.Value;
        var end = s.EndsAt!.Value;
        var svc = s.Service;
        var doc = s.Doctor;
        var room = s.Room;

        var status = ResolveEffectiveStatus(a, visit);

        var id = a.Id.ToString(vi);
        var appointmentCode = a.AppointmentCode ?? $"APT-{id}";

        var patientId = scopeMember.Patient.Id.ToString(vi);
        var patientCode = scopeMember.Patient.PatientCode;
        var patientName = scopeMember.Patient.FullName;
        var patientRelationshipLabel = scopeMember.RelationshipLabel;
        var patientAgeLabel = scopeMember.AgeLabel;

        var serviceId = svc?.Id.ToString(vi) ?? "";
        var serviceCode = svc?.ServiceCode ?? "";
        var serviceName = svc?.Name ?? "";
        var serviceFullLabel = $"{serviceCode}: {serviceName}";

        var specialtyIdValue = svc?.SpecialtyId ?? doc?.SpecialtyId;
        var specialtyId = specialtyIdValue?.ToString(vi) ?? "";
        var specialtyName = svc?.Specialty?.Name ?? doc?.Specialty?.Name ?? "";
        var departmentName = svc?.Department?.Name ?? room?.Department?.Name ?? "";

        var doctorId = doc?.Id.ToString(vi) ?? "";
        var doctorCode = doc?.DoctorCode ?? "";
        var doctorTitle = string.IsNullOrWhiteSpace(doc?.ProfessionalTitle) ? "BS" : doc.ProfessionalTitle.Trim();
        var doctorName = DoctorCatalogService.FormatDisplayName(doc?.Staff?.FullName, doctorTitle);
        var doctorRoleLabel = departmentName == ""
            ? "Bác sĩ phụ trách"
            : $"Bác sĩ phụ trách - {departmentName}";

        var roomCode = room?.RoomCode ?? "";
        var roomName = room?.Name ?? "";
        var roomZoneLabel = room?.Location ?? "";

        var scheduledDateOnly = DateOnly.FromDateTime(start);
        var scheduledDate = start.ToString("yyyy-MM-dd", vi);
        var ddMmYyyy = start.ToString("dd/MM/yyyy", vi);

        var scheduledDateBadge = scheduledDateOnly == today
            ? $"Hôm nay, {ddMmYyyy}"
            : scheduledDateOnly == today.AddDays(1)
                ? $"Ngày mai, {ddMmYyyy}"
                : ddMmYyyy;

        var shiftLabel = start.TimeOfDay < AfternoonShiftStart ? "Ca Sáng" : "Ca Chiều";
        var scheduledDateFullLabel = $"{AppointmentSlotService.FormatFullWeekday(start.DayOfWeek)}, {ddMmYyyy} ({shiftLabel})";
        var scheduledTimeRange = $"{start.ToString("HH:mm", vi)} - {end.ToString("HH:mm", vi)}";

        string? checkInDeadlineTime = status is "BOOKED" or "CONFIRMED" or "PENDING" && start >= nowLocal
            ? start.AddMinutes(-15).ToString("HH:mm", vi)
            : null;

        var queueNumber = queue?.QueueNumber?.ToString("D3", vi);
        var bookingChannel = string.Equals(a.BookingChannel?.Trim(), "COUNTER", StringComparison.OrdinalIgnoreCase)
            ? "COUNTER"
            : "ONLINE";

        var statusBannerLabel = ResolveStatusBannerLabel(status);

        var activePrice = svc?.ServicePrices?
            .Where(p => (!p.EffectiveFrom.HasValue || p.EffectiveFrom.Value <= start)
                     && (!p.EffectiveTo.HasValue || p.EffectiveTo.Value > start))
            .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(p => p.Id)
            .Select(p => p.PriceAmount)
            .FirstOrDefault() ?? 0m;

        var listedFee = (long)decimal.Truncate(activePrice);

        string paymentStatus;
        string paymentStatusLabel;
        string? paymentSubNote;

        if (paid is not null)
        {
            paymentStatus = "PAID_VIETQR";
            paymentStatusLabel = $"Đã thanh toán {ExaminationServiceCatalogService.FormatVndPrice(paid.Amount ?? 0m)} qua VietQR";
            paymentSubNote = null;
        }
        else if (IsCancelledStatus(status))
        {
            paymentStatus = "REFUNDED_OR_FREE";
            paymentStatusLabel = "Không phát sinh chi phí";
            paymentSubNote = null;
        }
        else
        {
            paymentStatus = "UNPAID_AT_COUNTER";
            paymentStatusLabel = "Chờ thanh toán tại quầy";
            paymentSubNote = IsUpcomingStatus(status)
                ? "Hoặc quét VietQR tự động khi check-in tại viện."
                : null;
        }

        var bookedAt = a.BookedAt is { } b
            ? new DateTimeOffset(DateTime.SpecifyKind(b, DateTimeKind.Unspecified), VietnamOffset)
                .ToString("yyyy-MM-ddTHH:mm:sszzz", vi)
            : "";

        return new AppointmentListItemDto(
            Id: id,
            AppointmentCode: appointmentCode,
            PatientId: patientId,
            PatientCode: patientCode,
            PatientName: patientName,
            PatientRelationshipLabel: patientRelationshipLabel,
            PatientAgeLabel: patientAgeLabel,
            ServiceId: serviceId,
            ServiceCode: serviceCode,
            ServiceName: serviceName,
            ServiceFullLabel: serviceFullLabel,
            SpecialtyId: specialtyId,
            SpecialtyName: specialtyName,
            DepartmentName: departmentName,
            DoctorId: doctorId,
            DoctorCode: doctorCode,
            DoctorName: doctorName,
            DoctorTitle: doctorTitle,
            DoctorRoleLabel: doctorRoleLabel,
            RoomCode: roomCode,
            RoomName: roomName,
            RoomZoneLabel: roomZoneLabel,
            ScheduledDate: scheduledDate,
            ScheduledDateBadge: scheduledDateBadge,
            ScheduledDateFullLabel: scheduledDateFullLabel,
            ScheduledTimeRange: scheduledTimeRange,
            CheckInDeadlineTime: checkInDeadlineTime,
            CheckInCounterLabel: null,
            QueueNumber: queueNumber,
            BookingChannel: bookingChannel,
            Status: status,
            StatusBannerLabel: statusBannerLabel,
            ListedFee: listedFee,
            FeePolicyNote: FeePolicyNoteText,
            PaymentStatus: paymentStatus,
            PaymentStatusLabel: paymentStatusLabel,
            PaymentSubNote: paymentSubNote,
            ReasonForVisit: visit?.ReasonForVisit ?? a.ReasonForVisit,
            ClinicalSummary: null,
            CancellationReason: status == "CANCELLED" ? a.CancellationReason : null,
            SlotReleaseNote: null,
            RebookSpecialtyLabel: null,
            BookedAt: bookedAt);
    }

    public static string ResolveEffectiveStatus(Appointment appointment, Visit? visit)
    {
        var raw = appointment.Status?.Trim().ToUpperInvariant();
        if (raw is "CANCELLED" or "NO_SHOW")
            return raw;

        var visitStatus = visit?.Status?.Trim().ToUpperInvariant();
        if (visitStatus == "COMPLETED" || raw == "COMPLETED")
            return "COMPLETED";

        if (raw is "CONFIRMED" or "BOOKED" or "PENDING" or "CHECKED_IN")
            return raw;

        return raw is null ? "BOOKED" : "PENDING";
    }

    public static bool IsUpcomingStatus(string status)
        => status is "CONFIRMED" or "BOOKED" or "PENDING" or "CHECKED_IN";

    public static bool IsCancelledStatus(string status)
        => status is "CANCELLED" or "NO_SHOW";

    public static bool IsModifiableStatus(string status)
        => status is "BOOKED" or "CONFIRMED" or "PENDING";

    public static bool IsWithinCancelWindow(DateTime startsAt, DateTime nowLocal, int cancelMinimumHours)
        => startsAt > nowLocal && startsAt >= nowLocal.AddHours(cancelMinimumHours);

    public static (string Trimmed, long? Id, string? Code) ParseIdentifier(string? idOrCode)
        => AppointmentBookingRules.ParseAppointmentIdentifier(idOrCode);

    public static string ResolveStatusBannerLabel(string status) => status switch
    {
        "BOOKED" => "ĐÃ ĐẶT LỊCH - CHỜ ĐẾN GIỜ KHÁM",
        "CONFIRMED" => "ĐÃ XÁC NHẬN - SẮP ĐẾN GIỜ KHÁM",
        "PENDING" => "ĐANG CHỜ XÁC NHẬN",
        "CHECKED_IN" => "ĐÃ CHECK-IN - ĐANG CHỜ KHÁM",
        "COMPLETED" => "ĐÃ HOÀN TẤT KHÁM",
        "CANCELLED" => "ĐÃ HỦY LỊCH",
        "NO_SHOW" => "VẮNG MẶT - KHÔNG ĐẾN KHÁM",
        _ => "ĐANG CHỜ XÁC NHẬN"
    };
}
