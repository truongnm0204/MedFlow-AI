using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentListService : IAppointmentListService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly TimeSpan AfternoonShiftStart = new(12, 0, 0);
    private const int MaxKeywordLength = 100;
    private const int MaxPageSize = 50;
    private const string FeePolicyNoteText = "Giá niêm yết dịch vụ khám • Không áp dụng BHYT";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public AppointmentListService(IApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<AppointmentListResponse> GetMyAppointmentsAsync(
        long userId,
        GetMyAppointmentsRequest request,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        ArgumentNullException.ThrowIfNull(request);

        var normalizedTab = ValidateAndNormalizeTab(request.Tab);
        var normalizedKeyword = ValidateAndNormalizeKeyword(request.Keyword);
        var (dateFrom, dateTo) = ValidateAndParseDateRange(request.DateFrom, request.DateTo);

        if (request.Page < 1)
            throw new ValidationException("Số trang phải lớn hơn hoặc bằng 1.");

        if (request.PageSize is < 1 or > MaxPageSize)
            throw new ValidationException("Kích thước trang phải từ 1 đến 50.");

        var targetMemberId = ValidateAndParseMemberId(request.MemberId);

        var nowLocal = GetVietnamNow();
        var today = DateOnly.FromDateTime(nowLocal);

        var self = await PatientAccessScope.LoadActiveSelfAsync(_db, userId, ct);

        var familyLinks = await _db.PatientFamilyLinks
            .AsNoTracking()
            .Include(l => l.MemberPatient)
            .WhereActiveMemberOf(self.Id, nowLocal)
            .OrderBy(l => l.MemberPatientId)
            .ToListAsync(ct);

        var scope = BuildScopeMembers(self, familyLinks, today);
        var scopeById = scope.ToDictionary(m => m.Patient.Id);

        if (targetMemberId.HasValue && !scopeById.ContainsKey(targetMemberId.Value))
            throw new ForbiddenException("Bạn không có quyền xem lịch hẹn của hồ sơ này.");

        var scopeIds = scope.Select(m => m.Patient.Id).ToList();

        var appointments = await _db.Appointments
            .AsNoTracking()
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
                    .ThenInclude(r => r!.Department)
            .Where(a =>
                a.PatientId != null &&
                scopeIds.Contains(a.PatientId.Value) &&
                a.Slot != null &&
                a.Slot.StartsAt != null &&
                a.Slot.EndsAt != null)
            .ToListAsync(ct);

        Dictionary<long, Visit> visitsByApptId = [];
        Dictionary<long, QueueEntry> queueByApptId = [];
        Dictionary<long, Payment> paidByVisitId = [];

        if (appointments.Count > 0)
        {
            var apptIds = appointments.Select(a => a.Id).ToList();

            var visits = await _db.Visits
                .AsNoTracking()
                .Where(v => v.AppointmentId != null && apptIds.Contains(v.AppointmentId.Value))
                .ToListAsync(ct);

            visitsByApptId = visits
                .GroupBy(v => v.AppointmentId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.Id).First());

            var queueEntries = await _db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    q.AppointmentId != null &&
                    apptIds.Contains(q.AppointmentId.Value) &&
                    q.QueueType != null &&
                    q.QueueType.ToUpper() == "EXAMINATION")
                .ToListAsync(ct);

            queueByApptId = queueEntries
                .GroupBy(q => q.AppointmentId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(q => q.Id).First());

            if (visitsByApptId.Count > 0)
            {
                var visitIds = visitsByApptId.Values.Select(v => v.Id).ToList();

                var payments = await _db.Payments
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

        var mappedItems = new List<MappedAppointment>(appointments.Count);
        foreach (var appointment in appointments)
        {
            var patientId = appointment.PatientId!.Value;
            if (!scopeById.TryGetValue(patientId, out var scopeMember))
                continue;

            visitsByApptId.TryGetValue(appointment.Id, out var visit);
            queueByApptId.TryGetValue(appointment.Id, out var queueEntry);
            Payment? paid = null;
            if (visit is not null)
                paidByVisitId.TryGetValue(visit.Id, out paid);

            var mapped = MapAppointmentItem(
                appointment,
                scopeMember,
                visit,
                queueEntry,
                paid,
                nowLocal,
                today);

            mappedItems.Add(mapped);
        }

        var countByPatientId = mappedItems
            .GroupBy(x => x.PatientId)
            .ToDictionary(g => g.Key, g => g.Count());

        var vi = CultureInfo.InvariantCulture;
        var memberCounts = scope
            .Select(m => new AppointmentMemberCountDto(
                Id: m.Patient.Id.ToString(vi),
                PatientCode: m.Patient.PatientCode,
                FullName: m.Patient.FullName,
                RelationshipLabel: m.RelationshipLabel,
                AgeLabel: m.AgeLabel,
                AppointmentCount: countByPatientId.GetValueOrDefault(m.Patient.Id, 0)))
            .ToList();

        var totalAllMembersCount = memberCounts.Sum(m => m.AppointmentCount);

        IEnumerable<MappedAppointment> filtered = mappedItems;

        if (targetMemberId.HasValue)
            filtered = filtered.Where(x => x.PatientId == targetMemberId.Value);

        if (!string.IsNullOrEmpty(normalizedKeyword))
            filtered = filtered.Where(x => MatchesKeyword(x.Dto, normalizedKeyword));

        if (dateFrom.HasValue)
            filtered = filtered.Where(x => x.ScheduledDateOnly >= dateFrom.Value);

        if (dateTo.HasValue)
            filtered = filtered.Where(x => x.ScheduledDateOnly <= dateTo.Value);

        var preTabList = filtered.ToList();

        var tabCounts = new AppointmentTabCountsDto(
            All: preTabList.Count,
            Upcoming: preTabList.Count(x => IsUpcomingStatus(x.Dto.Status)),
            Completed: preTabList.Count(x => x.Dto.Status == "COMPLETED"),
            Cancelled: preTabList.Count(x => IsCancelledStatus(x.Dto.Status)));

        IEnumerable<MappedAppointment> tabFiltered = normalizedTab switch
        {
            "UPCOMING" => preTabList.Where(x => IsUpcomingStatus(x.Dto.Status)),
            "COMPLETED" => preTabList.Where(x => x.Dto.Status == "COMPLETED"),
            "CANCELLED" => preTabList.Where(x => IsCancelledStatus(x.Dto.Status)),
            _ => preTabList
        };

        var sorted = normalizedTab == "UPCOMING"
            ? tabFiltered.OrderBy(x => x.StartsAt).ThenBy(x => x.AppointmentId).ToList()
            : tabFiltered.OrderByDescending(x => x.StartsAt).ThenByDescending(x => x.AppointmentId).ToList();

        var total = sorted.Count;
        var totalPages = total == 0 ? 1 : (int)Math.Ceiling((double)total / request.PageSize);

        var pagedItems = sorted
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => x.Dto)
            .ToList();

        return new AppointmentListResponse(
            Items: pagedItems,
            Total: total,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: totalPages,
            TabCounts: tabCounts,
            MemberCounts: memberCounts,
            TotalAllMembersCount: totalAllMembersCount);
    }

    private DateTime GetVietnamNow()
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        return DateTime.SpecifyKind(utcNow.Add(VietnamOffset), DateTimeKind.Unspecified);
    }

    private static string ValidateAndNormalizeTab(string? rawTab)
    {
        if (string.IsNullOrWhiteSpace(rawTab))
            return "ALL";

        var upper = rawTab.Trim().ToUpperInvariant();
        return upper switch
        {
            "ALL" or "UPCOMING" or "COMPLETED" or "CANCELLED" => upper,
            _ => throw new ValidationException("Tab trạng thái không hợp lệ.")
        };
    }

    private static string? ValidateAndNormalizeKeyword(string? rawKeyword)
    {
        if (string.IsNullOrWhiteSpace(rawKeyword))
            return null;

        var trimmed = rawKeyword.Trim();
        if (trimmed.Length > MaxKeywordLength)
            throw new ValidationException("Từ khóa tìm kiếm không được vượt quá 100 ký tự.");

        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].TrimStart();

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static (DateOnly? DateFrom, DateOnly? DateTo) ValidateAndParseDateRange(
        string? rawDateFrom,
        string? rawDateTo)
    {
        DateOnly? dateFrom = null;
        DateOnly? dateTo = null;

        if (!string.IsNullOrWhiteSpace(rawDateFrom))
        {
            if (!DateOnly.TryParseExact(
                    rawDateFrom.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedFrom))
            {
                throw new ValidationException("Ngày lọc không hợp lệ (định dạng yyyy-MM-dd).");
            }

            dateFrom = parsedFrom;
        }

        if (!string.IsNullOrWhiteSpace(rawDateTo))
        {
            if (!DateOnly.TryParseExact(
                    rawDateTo.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedTo))
            {
                throw new ValidationException("Ngày lọc không hợp lệ (định dạng yyyy-MM-dd).");
            }

            dateTo = parsedTo;
        }

        if (dateFrom.HasValue && dateTo.HasValue && dateFrom.Value > dateTo.Value)
            throw new ValidationException("Ngày bắt đầu không được sau ngày kết thúc.");

        return (dateFrom, dateTo);
    }

    private static long? ValidateAndParseMemberId(string? rawMemberId)
    {
        if (string.IsNullOrWhiteSpace(rawMemberId))
            return null;

        var trimmed = rawMemberId.Trim();
        if (string.Equals(trimmed, "ALL", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var memberId) || memberId <= 0)
            throw new ValidationException("Mã hồ sơ thành viên không hợp lệ.");

        return memberId;
    }

    private static List<ScopeMember> BuildScopeMembers(
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

    private static string FormatRelationshipLabel(string? relationship)
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

    private static string FormatAgeLabel(DateOnly? dob, DateOnly today)
        => dob is null
            ? "Chưa rõ tuổi"
            : $"{AppointmentSlotService.CalculateAge(dob, today).ToString(CultureInfo.InvariantCulture)} tuổi";

    private static MappedAppointment MapAppointmentItem(
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

        var dto = new AppointmentListItemDto(
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
            CancellationReason: null,
            SlotReleaseNote: null,
            RebookSpecialtyLabel: null,
            BookedAt: bookedAt);

        return new MappedAppointment(a.Id, scopeMember.Patient.Id, start, scheduledDateOnly, dto);
    }

    private static string ResolveEffectiveStatus(Appointment appointment, Visit? visit)
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

    private static bool IsUpcomingStatus(string status)
        => status is "CONFIRMED" or "BOOKED" or "PENDING" or "CHECKED_IN";

    private static bool IsCancelledStatus(string status)
        => status is "CANCELLED" or "NO_SHOW";

    private static string ResolveStatusBannerLabel(string status) => status switch
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

    private static bool MatchesKeyword(AppointmentListItemDto dto, string keyword)
        => dto.AppointmentCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.DoctorName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.SpecialtyName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.ServiceName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.ServiceCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.PatientName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || dto.RoomName.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private sealed record ScopeMember(
        Patient Patient,
        string RelationshipLabel,
        string AgeLabel);

    private sealed record MappedAppointment(
        long AppointmentId,
        long PatientId,
        DateTime StartsAt,
        DateOnly ScheduledDateOnly,
        AppointmentListItemDto Dto);
}
