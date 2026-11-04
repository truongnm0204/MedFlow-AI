using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentSlotService : IAppointmentSlotService
{
    private const int StripDays = 7;
    private const int FewSlotsThreshold = 5;

    private const string MorningShiftDescription =
        "Phù hợp khi cần thực hiện xét nghiệm máu nhịn ăn sáng, siêu âm ổ bụng hoặc nội soi tiêu hóa trong ngày.";

    private const string AfternoonShiftDescription =
        "Khám chuyên khoa ngoại trú, tư vấn kết quả cận lâm sàng và kê đơn thuốc điều trị ngoại trú thông thoáng.";

    private readonly IApplicationDbContext _db;
    private readonly IDoctorCatalogService _doctorCatalog;
    private readonly IExaminationServiceCatalogService _serviceCatalog;
    private readonly IAppointmentPolicyService _policyService;
    private readonly TimeProvider _timeProvider;

    public AppointmentSlotService(
        IApplicationDbContext db,
        IDoctorCatalogService doctorCatalog,
        IExaminationServiceCatalogService serviceCatalog,
        IAppointmentPolicyService policyService,
        TimeProvider timeProvider)
    {
        _db = db;
        _doctorCatalog = doctorCatalog;
        _serviceCatalog = serviceCatalog;
        _policyService = policyService;
        _timeProvider = timeProvider;
    }

    public async Task<AvailableSlotsResponse> GetAvailableSlotsAsync(
        long userId,
        GetAvailableSlotsRequest request,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        ArgumentNullException.ThrowIfNull(request);

        var requestedDate = ParseRequestedDate(request.DateIso);

        if (string.IsNullOrWhiteSpace(request.DoctorId))
            throw new ValidationException("Vui lòng chọn bác sĩ.");

        var doctorDto = await _doctorCatalog.GetByIdOrCodeAsync(request.DoctorId, ct);
        var doctorId = long.Parse(doctorDto.Id, CultureInfo.InvariantCulture);

        ExaminationServiceDto serviceDto;
        long serviceId;

        if (!string.IsNullOrWhiteSpace(request.ServiceId))
        {
            serviceDto = await _serviceCatalog.GetByIdOrCodeAsync(request.ServiceId, ct);
            serviceId = long.Parse(serviceDto.Id, CultureInfo.InvariantCulture);

            var isAuthorized = await _db.DoctorServiceAuthorizations
                .AsNoTracking()
                .AnyAsync(a => a.DoctorId == doctorId && a.ServiceId == serviceId && a.IsActive == true, ct);

            if (!isAuthorized)
                throw new ValidationException("Bác sĩ không thực hiện dịch vụ khám này.");
        }
        else
        {
            var authorizedServices = await _db.DoctorServiceAuthorizations
                .AsNoTracking()
                .Include(a => a.Service)
                .Where(a =>
                    a.DoctorId == doctorId &&
                    a.IsActive == true &&
                    a.Service != null &&
                    a.Service.IsActive == true &&
                    a.Service.ServiceType.ToUpper() == "EXAMINATION")
                .Select(a => new { a.ServiceId, ServiceCode = a.Service!.ServiceCode })
                .ToListAsync(ct);

            var primaryService = authorizedServices
                .OrderBy(s => s.ServiceCode.EndsWith("-01", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(s => s.ServiceId)
                .FirstOrDefault();

            if (primaryService is null)
                throw new ValidationException("Bác sĩ chưa được gán dịch vụ khám.");

            serviceId = primaryService.ServiceId;
            serviceDto = await _serviceCatalog.GetByIdOrCodeAsync(
                serviceId.ToString(CultureInfo.InvariantCulture),
                ct);
        }

        if (!serviceDto.IsOnlineBookingActive)
            throw new ValidationException("Dịch vụ khám này không hỗ trợ đặt lịch trực tuyến.");

        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var profiles = await LoadPatientProfilesAsync(userId, nowLocal, today, ct);
        var hospitalId = await AppointmentBookingRules.ResolveHospitalIdAsync(_db, serviceId, doctorId, ct);
        var policy = await _policyService.GetEffectiveAsync(hospitalId, ct);

        var bookableSlots = await LoadBookableSlotsAsync(
            serviceId,
            doctorId,
            nowLocal,
            today,
            policy.BookingLeadHours,
            policy.BookingWindowDays,
            ct);

        var dates = BuildDateOptions(today, bookableSlots, isDoctorMode: true);
        var effectiveDate = ResolveEffectiveDate(today, requestedDate, dates);
        var monthLabel = $"Tháng {effectiveDate.Month}, {effectiveDate.Year}";

        var effectiveDaySlots = bookableSlots
            .Where(s => DateOnly.FromDateTime(s.StartsAt) == effectiveDate)
            .ToList();

        var morningRaw = effectiveDaySlots.Where(s => s.Shift == "MORNING").ToList();
        var afternoonRaw = effectiveDaySlots.Where(s => s.Shift == "AFTERNOON").ToList();

        var morningSlots = morningRaw.Select(MapToTimeSlotDto).ToList();
        var afternoonSlots = afternoonRaw.Select(MapToTimeSlotDto).ToList();

        var activeDayShift = BuildDoctorDayShift(
            effectiveDate,
            morningRaw,
            afternoonRaw,
            morningSlots,
            afternoonSlots);

        var doctorService = BuildDoctorServiceInfo(doctorDto, serviceDto, bookableSlots);

        return new AvailableSlotsResponse(
            DoctorService: doctorService,
            MonthLabel: monthLabel,
            Profiles: profiles,
            Dates: dates,
            MorningSlots: morningSlots,
            AfternoonSlots: afternoonSlots,
            ActiveDayShift: activeDayShift);
    }

    public async Task<ServiceShiftsResponse> GetServiceShiftsAsync(
        long userId,
        GetServiceShiftsRequest request,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        ArgumentNullException.ThrowIfNull(request);

        var requestedDate = ParseRequestedDate(request.DateIso);

        if (string.IsNullOrWhiteSpace(request.ServiceId))
            throw new ValidationException("Vui lòng chọn dịch vụ khám.");

        var serviceDto = await _serviceCatalog.GetByIdOrCodeAsync(request.ServiceId, ct);
        if (!serviceDto.IsOnlineBookingActive)
            throw new ValidationException("Dịch vụ khám này không hỗ trợ đặt lịch trực tuyến.");

        var serviceId = long.Parse(serviceDto.Id, CultureInfo.InvariantCulture);

        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var profiles = await LoadPatientProfilesAsync(userId, nowLocal, today, ct);
        var hospitalId = await AppointmentBookingRules.ResolveHospitalIdAsync(_db, serviceId, doctorId: null, ct);
        var policy = await _policyService.GetEffectiveAsync(hospitalId, ct);

        var bookableSlots = await LoadBookableSlotsAsync(
            serviceId,
            doctorId: null,
            nowLocal,
            today,
            policy.BookingLeadHours,
            policy.BookingWindowDays,
            ct);

        var dates = BuildDateOptions(today, bookableSlots, isDoctorMode: false);
        var effectiveDate = ResolveEffectiveDate(today, requestedDate, dates);
        var monthLabel = $"Tháng {effectiveDate.Month}, {effectiveDate.Year}";

        var effectiveDaySlots = bookableSlots
            .Where(s => DateOnly.FromDateTime(s.StartsAt) == effectiveDate)
            .GroupBy(s => (s.StartsAt, s.EndsAt, s.Shift))
            .OrderBy(g => g.Key.StartsAt)
            .Select(g =>
            {
                var rep = g.FirstOrDefault(x => x.Remaining > 0) ?? g.First();
                return new BookableSlotItem(
                    rep.Id,
                    g.Key.StartsAt,
                    g.Key.EndsAt,
                    g.Sum(x => x.Remaining),
                    g.Key.Shift);
            })
            .ToList();

        var morningRaw = effectiveDaySlots.Where(s => s.Shift == "MORNING").ToList();
        var afternoonRaw = effectiveDaySlots.Where(s => s.Shift == "AFTERNOON").ToList();
        var morningSlots = morningRaw.Select(MapToTimeSlotDto).ToList();
        var afternoonSlots = afternoonRaw.Select(MapToTimeSlotDto).ToList();
        var activeDayShift = BuildDoctorDayShift(
            effectiveDate,
            morningRaw,
            afternoonRaw,
            morningSlots,
            afternoonSlots);

        var shifts = BuildServiceShifts(effectiveDaySlots);
        var serviceInfo = BuildServiceInfo(serviceDto);

        return new ServiceShiftsResponse(
            ServiceInfo: serviceInfo,
            MonthLabel: monthLabel,
            Profiles: profiles,
            Dates: dates,
            Shifts: shifts,
            MorningSlots: morningSlots,
            AfternoonSlots: afternoonSlots,
            ActiveDayShift: activeDayShift);
    }

    private static DateOnly? ParseRequestedDate(string? dateIso)
    {
        if (string.IsNullOrWhiteSpace(dateIso))
            return null;

        var trimmed = dateIso.Trim();
        if (!DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ValidationException("Ngày khám không hợp lệ (định dạng yyyy-MM-dd).");

        return parsed;
    }

    private async Task<List<BookableSlotItem>> LoadBookableSlotsAsync(
        long serviceId,
        long? doctorId,
        DateTime nowLocal,
        DateOnly today,
        int bookingLeadHours,
        int bookingWindowDays,
        CancellationToken ct)
    {
        var stripStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var stripEnd = stripStart.AddDays(StripDays);

        var query = _db.ExaminationSlots
            .AsNoTracking()
            .Where(s =>
                s.ServiceId == serviceId &&
                s.Status.ToUpper() == "OPEN" &&
                s.StartsAt != null &&
                s.EndsAt != null &&
                s.StartsAt >= stripStart &&
                s.StartsAt < stripEnd);

        if (doctorId.HasValue)
        {
            var docId = doctorId.Value;
            query = query.Where(s => s.DoctorId == docId);
        }

        var rawSlots = await query
            .OrderBy(s => s.StartsAt)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        var minStartTime = nowLocal.AddHours(bookingLeadHours);
        var maxDate = today.AddDays(bookingWindowDays);

        var filteredSlots = rawSlots
            .Where(s =>
                s.StartsAt!.Value >= minStartTime &&
                DateOnly.FromDateTime(s.StartsAt.Value) <= maxDate)
            .ToList();

        if (filteredSlots.Count == 0)
            return new List<BookableSlotItem>();

        var slotIds = filteredSlots.Select(s => s.Id).ToList();

        var bookedCounts = await _db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.SlotId != null &&
                slotIds.Contains((long)a.SlotId) &&
                (a.Status == null || a.Status.ToUpper() != AppointmentBookingRules.CancelledStatus))
            .GroupBy(a => a.SlotId!.Value)
            .Select(g => new { SlotId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SlotId, x => x.Count, ct);

        return filteredSlots
            .Select(s =>
            {
                var booked = bookedCounts.GetValueOrDefault(s.Id, 0);
                var remaining = Math.Max(0, (s.Capacity ?? 0) - booked);
                var startsAt = s.StartsAt!.Value;
                var endsAt = s.EndsAt!.Value;
                var shift = startsAt.TimeOfDay < AppointmentBookingRules.AfternoonShiftStart ? "MORNING" : "AFTERNOON";

                return new BookableSlotItem(s.Id, startsAt, endsAt, remaining, shift);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<BookingPatientProfileDto>> LoadPatientProfilesAsync(
        long userId,
        DateTime nowLocal,
        DateOnly today,
        CancellationToken ct)
    {
        var (self, familyLinks) = await AppointmentBookingRules.LoadOwnedPatientsAsync(_db, userId, nowLocal, ct);
        var patientIds = new List<long>(1 + familyLinks.Count) { self.Id };
        patientIds.AddRange(familyLinks.Select(l => l.MemberPatientId));

        var activeBookedDatesByPatient = await LoadActiveBookedDatesByPatientAsync(patientIds, today, ct);

        var profiles = new List<BookingPatientProfileDto>(1 + familyLinks.Count)
        {
            MapSelfProfile(self, today, activeBookedDatesByPatient.GetValueOrDefault(self.Id, Array.Empty<string>()))
        };

        foreach (var link in familyLinks)
        {
            profiles.Add(MapMemberProfile(
                link.MemberPatient!,
                link.Relationship,
                today,
                activeBookedDatesByPatient.GetValueOrDefault(link.MemberPatientId, Array.Empty<string>())));
        }

        return profiles;
    }

    private async Task<Dictionary<long, IReadOnlyList<string>>> LoadActiveBookedDatesByPatientAsync(
        List<long> patientIds,
        DateOnly today,
        CancellationToken ct)
    {
        var stripStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var stripEnd = stripStart.AddDays(StripDays);

        var rawAppointments = await _db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.PatientId != null &&
                patientIds.Contains(a.PatientId.Value) &&
                a.Slot != null &&
                a.Slot.StartsAt != null &&
                a.Slot.StartsAt >= stripStart &&
                a.Slot.StartsAt < stripEnd)
            .Select(a => new
            {
                a.Id,
                PatientId = a.PatientId!.Value,
                a.Status,
                StartsAt = a.Slot!.StartsAt!.Value
            })
            .ToListAsync(ct);

        var apptIds = rawAppointments.Select(a => a.Id).ToList();
        var visitsByApptId = new Dictionary<long, List<Visit>>();
        if (apptIds.Count > 0)
        {
            var visits = await _db.Visits
                .AsNoTracking()
                .Where(v => v.AppointmentId != null && apptIds.Contains(v.AppointmentId.Value))
                .ToListAsync(ct);

            visitsByApptId = visits
                .GroupBy(v => v.AppointmentId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        var result = new Dictionary<long, HashSet<string>>();
        foreach (var appt in rawAppointments)
        {
            var statusKey = AppointmentBookingRules.NormalizeStatusKey(appt.Status);
            if (string.IsNullOrEmpty(statusKey))
                statusKey = AppointmentBookingRules.BookedStatus;

            var linkedVisits = visitsByApptId.GetValueOrDefault(appt.Id);
            var hasActiveVisit = linkedVisits is not null &&
                                 linkedVisits.Any(v => AppointmentBookingRules.IsActiveVisitStatus(v.Status));
            var hasCompletedVisit = linkedVisits is not null &&
                                    linkedVisits.Any(v => AppointmentBookingRules.IsCompletedVisitStatus(v.Status));
            var hasCancelledVisitOnly = linkedVisits is { Count: > 0 } &&
                                        linkedVisits.All(v => AppointmentBookingRules.NormalizeStatusKey(v.Status) == AppointmentBookingRules.CancelledStatus);

            bool blocksDate;
            if (hasActiveVisit)
            {
                blocksDate = true;
            }
            else if (statusKey is AppointmentBookingRules.CancelledStatus or AppointmentBookingRules.NoShowStatus or AppointmentBookingRules.CompletedStatus)
            {
                blocksDate = false;
            }
            else if (statusKey == AppointmentBookingRules.CheckedInStatus)
            {
                blocksDate = !hasCompletedVisit && !hasCancelledVisitOnly;
            }
            else
            {
                blocksDate = !hasCompletedVisit;
            }

            if (!blocksDate)
                continue;

            if (!result.TryGetValue(appt.PatientId, out var dates))
            {
                dates = new HashSet<string>(StringComparer.Ordinal);
                result[appt.PatientId] = dates;
            }

            dates.Add(DateOnly.FromDateTime(appt.StartsAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return result.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value.OrderBy(d => d).ToList());
    }

    private static BookingPatientProfileDto MapSelfProfile(
        Patient self,
        DateOnly today,
        IReadOnlyList<string> activeBookedDates)
    {
        var age = CalculateAge(self.DateOfBirth, today);
        var gender = FormatGender(self.Gender);
        var metaPrimary = !string.IsNullOrWhiteSpace(self.Phone)
            ? $"SĐT: {self.Phone}"
            : "Chưa cập nhật SĐT";
        var metaSecondary = !string.IsNullOrWhiteSpace(self.IdentificationHash)
            ? "Định danh đã đối chiếu"
            : "Chưa xác thực định danh";

        return new BookingPatientProfileDto(
            Id: self.Id.ToString(CultureInfo.InvariantCulture),
            FullName: self.FullName,
            RelationshipLabel: "(Bản thân)",
            BadgeText: "Chính chủ",
            BadgeVariant: "primary",
            Gender: gender,
            Age: age,
            MetaPrimary: metaPrimary,
            MetaSecondary: metaSecondary,
            PatientCode: self.PatientCode,
            ActiveBookedDates: activeBookedDates);
    }

    private static BookingPatientProfileDto MapMemberProfile(
        Patient member,
        string? relationship,
        DateOnly today,
        IReadOnlyList<string> activeBookedDates)
    {
        var age = CalculateAge(member.DateOfBirth, today);
        var gender = FormatGender(member.Gender);
        var relationshipLabel = FormatRelationshipLabel(relationship);

        var (badgeText, badgeVariant) = age switch
        {
            < 16 => ("Nhi khoa", "pediatric"),
            >= 60 => ("Lão khoa", "geriatric"),
            _ => ("Người thân", "primary")
        };

        var metaPrimary = !string.IsNullOrWhiteSpace(member.Phone)
            ? $"SĐT: {member.Phone}"
            : "Hồ sơ người thân";
        var metaSecondary = $"PID: {member.PatientCode}";

        return new BookingPatientProfileDto(
            Id: member.Id.ToString(CultureInfo.InvariantCulture),
            FullName: member.FullName,
            RelationshipLabel: relationshipLabel,
            BadgeText: badgeText,
            BadgeVariant: badgeVariant,
            Gender: gender,
            Age: age,
            MetaPrimary: metaPrimary,
            MetaSecondary: metaSecondary,
            PatientCode: member.PatientCode,
            ActiveBookedDates: activeBookedDates);
    }

    internal static int CalculateAge(DateOnly? dateOfBirth, DateOnly today)
    {
        if (!dateOfBirth.HasValue)
            return 0;

        var dob = dateOfBirth.Value;
        var age = today.Year - dob.Year;

        if (dob > today.AddYears(-age))
            age--;

        return Math.Max(0, age);
    }

    internal static string FormatGender(string? gender)
        => string.Equals(gender, "FEMALE", StringComparison.OrdinalIgnoreCase) ? "Nữ" : "Nam";

    internal static string FormatRelationshipLabel(string? relationship)
    {
        return relationship?.Trim().ToUpperInvariant() switch
        {
            "CHILD" => "(Con)",
            "SPOUSE" => "(Vợ/Chồng)",
            "PARENT" => "(Cha/Mẹ)",
            "SIBLING" => "(Anh/Chị/Em)",
            _ => "(Người thân)"
        };
    }

    private static List<BookingDateOptionDto> BuildDateOptions(
        DateOnly today,
        List<BookableSlotItem> bookableSlots,
        bool isDoctorMode)
    {
        var dates = new List<BookingDateOptionDto>(StripDays);

        for (var i = 0; i < StripDays; i++)
        {
            var d = today.AddDays(i);
            var daySlots = bookableSlots
                .Where(s => DateOnly.FromDateTime(s.StartsAt) == d)
                .ToList();

            var rem = daySlots.Sum(s => s.Remaining);
            var dateIso = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var dayTopLabel = d == today ? "Hôm nay" : FormatDayTopLabel(d.DayOfWeek);
            var dayNumber = d.ToString("dd", CultureInfo.InvariantCulture);
            var subDateLabel = d == today.AddDays(1)
                ? "Ngày mai"
                : $"{d.ToString("dd", CultureInfo.InvariantCulture)} Thg {d.Month}";
            var fullDateDisplay = $"{FormatFullWeekday(d.DayOfWeek)}, {d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

            string status;
            string badgeText;

            if (daySlots.Count == 0)
            {
                status = "OFF";
                badgeText = "Không có lịch khám";
            }
            else if (rem == 0)
            {
                status = "FULL";
                badgeText = "Hết chỗ";
            }
            else if (rem <= FewSlotsThreshold)
            {
                status = "FEW_SLOTS";
                badgeText = isDoctorMode ? $"Còn {rem} slot" : $"Còn {rem} lượt";
            }
            else
            {
                status = "AVAILABLE";
                badgeText = isDoctorMode ? $"Còn {rem} slot" : $"Còn {rem} lượt";
            }

            string? doctorShiftSummary = null;
            if (isDoctorMode)
            {
                var hasMorning = daySlots.Any(s => s.Shift == "MORNING");
                var hasAfternoon = daySlots.Any(s => s.Shift == "AFTERNOON");

                doctorShiftSummary = (hasMorning, hasAfternoon) switch
                {
                    (true, true) => "Trực cả ngày",
                    (true, false) => "Chỉ trực Ca Sáng",
                    (false, true) => "Chỉ trực Ca Chiều",
                    (false, false) => "Nghỉ trực"
                };
            }

            dates.Add(new BookingDateOptionDto(
                DateIso: dateIso,
                DayTopLabel: dayTopLabel,
                DayNumber: dayNumber,
                SubDateLabel: subDateLabel,
                FullDateDisplay: fullDateDisplay,
                Status: status,
                RemainingSlots: rem,
                BadgeText: badgeText,
                IsToday: d == today,
                DoctorShiftSummary: doctorShiftSummary));
        }

        return dates;
    }

    private static DateOnly ResolveEffectiveDate(
        DateOnly today,
        DateOnly? requestedDate,
        List<BookingDateOptionDto> dates)
    {
        var stripEndDate = today.AddDays(StripDays - 1);
        if (requestedDate.HasValue && requestedDate.Value >= today && requestedDate.Value <= stripEndDate)
        {
            var requestedIso = requestedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var matched = dates.FirstOrDefault(d => d.DateIso == requestedIso);
            if (matched is not null && matched.Status is "AVAILABLE" or "FEW_SLOTS")
                return requestedDate.Value;
        }

        var firstAvailable = dates.FirstOrDefault(d => d.Status is "AVAILABLE" or "FEW_SLOTS");
        if (firstAvailable is not null)
            return DateOnly.ParseExact(firstAvailable.DateIso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (requestedDate.HasValue && requestedDate.Value >= today && requestedDate.Value <= stripEndDate)
            return requestedDate.Value;

        return today;
    }

    private static BookingTimeSlotDto MapToTimeSlotDto(BookableSlotItem slot)
    {
        var (status, badgeText) = slot.Remaining switch
        {
            0 => ("FULL", "Hết chỗ"),
            1 => ("LAST_SLOT", "Còn 1 slot"),
            var n => ("AVAILABLE", $"Còn {n} slot")
        };

        return new BookingTimeSlotDto(
            Id: slot.Id.ToString(CultureInfo.InvariantCulture),
            TimeRange: $"{slot.StartsAt:HH:mm} - {slot.EndsAt:HH:mm}",
            Shift: slot.Shift,
            ShiftLabel: slot.Shift == "MORNING" ? "Ca Sáng" : "Ca Chiều",
            Status: status,
            RemainingSlots: slot.Remaining,
            BadgeText: badgeText);
    }

    private static DoctorDayShiftDto BuildDoctorDayShift(
        DateOnly effectiveDate,
        List<BookableSlotItem> morningRaw,
        List<BookableSlotItem> afternoonRaw,
        List<BookingTimeSlotDto> morningSlots,
        List<BookingTimeSlotDto> afternoonSlots)
    {
        var hasMorning = morningSlots.Count > 0;
        var hasAfternoon = afternoonSlots.Count > 0;
        var dm = effectiveDate.ToString("dd/MM", CultureInfo.InvariantCulture);

        var ms = hasMorning
            ? $"{morningRaw.Min(s => s.StartsAt):HH:mm} - {morningRaw.Max(s => s.EndsAt):HH:mm}"
            : string.Empty;
        var asRange = hasAfternoon
            ? $"{afternoonRaw.Min(s => s.StartsAt):HH:mm} - {afternoonRaw.Max(s => s.EndsAt):HH:mm}"
            : string.Empty;

        string shiftSummaryLabel;
        string? offShiftNote;

        switch (hasMorning, hasAfternoon)
        {
            case (true, true):
                shiftSummaryLabel = $"Ca trực Bác sĩ: Trực cả ngày — Ca Sáng ({ms}) & Ca Chiều ({asRange})";
                offShiftNote = null;
                break;
            case (true, false):
                shiftSummaryLabel = $"Ca trực Bác sĩ ngày {dm}: Chỉ trực Ca Sáng ({ms})";
                offShiftNote = $"Bác sĩ không mở lịch Ca Chiều ngày {dm} — chỉ đặt được Ca Sáng.";
                break;
            case (false, true):
                shiftSummaryLabel = $"Ca trực Bác sĩ ngày {dm}: Chỉ trực Ca Chiều ({asRange})";
                offShiftNote = $"Bác sĩ không mở lịch Ca Sáng ngày {dm} — chỉ đặt được Ca Chiều.";
                break;
            default:
                shiftSummaryLabel = $"Ca trực Bác sĩ ngày {dm}: Không có lịch khám";
                offShiftNote = $"Bác sĩ không có lịch khám ngày {dm}. Vui lòng chọn ngày khác.";
                break;
        }

        return new DoctorDayShiftDto(
            HasMorningShift: hasMorning,
            HasAfternoonShift: hasAfternoon,
            ShiftSummaryLabel: shiftSummaryLabel,
            OffShiftNote: offShiftNote,
            MorningSlots: morningSlots,
            AfternoonSlots: afternoonSlots);
    }

    private static BookingDoctorServiceInfoDto BuildDoctorServiceInfo(
        DoctorDetailDto doctorDto,
        ExaminationServiceDto serviceDto,
        List<BookableSlotItem> bookableSlots)
    {
        var room = doctorDto.Room;
        var shortRoom = room;
        if (room.StartsWith("Phòng ", StringComparison.Ordinal))
        {
            var sepIndex = room.IndexOf(" - ", StringComparison.Ordinal);
            if (sepIndex > 0)
                shortRoom = room[..sepIndex];
        }

        var price = serviceDto.Price > 0 ? serviceDto.Price : doctorDto.Price;
        var availabilityLabel = bookableSlots.Any(s => s.Remaining > 0)
            ? "Sẵn sàng"
            : "Tạm hết lịch";

        var dutyNote = bookableSlots.Count > 0
            ? $"Ca trực gần nhất: {bookableSlots[0].StartsAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} ({bookableSlots[0].StartsAt:HH:mm} - {bookableSlots[0].EndsAt:HH:mm})"
            : "Chưa có lịch trực trong 7 ngày tới";

        return new BookingDoctorServiceInfoDto(
            DoctorId: doctorDto.Id,
            DoctorName: doctorDto.Name,
            RoleTag: doctorDto.RoleTitle,
            Specialty: doctorDto.Specialty,
            Room: room,
            ShortRoom: shortRoom,
            ServiceId: serviceDto.Id,
            ServiceCode: serviceDto.ServiceCode,
            ServiceName: serviceDto.Name,
            Price: price,
            AvatarUrl: doctorDto.AvatarUrl,
            AvailabilityLabel: availabilityLabel,
            DutyNote: dutyNote);
    }

    private static BookingServiceInfoDto BuildServiceInfo(ExaminationServiceDto serviceDto)
    {
        return new BookingServiceInfoDto(
            ServiceId: serviceDto.Id,
            ServiceCode: serviceDto.ServiceCode,
            ServiceName: serviceDto.Name,
            DepartmentName: serviceDto.DepartmentName,
            Location: serviceDto.Location ?? string.Empty,
            DurationMinutes: serviceDto.DurationMinutes,
            DurationDescription: serviceDto.DurationDescription,
            Price: serviceDto.Price,
            OnlineBadgeLabel: serviceDto.BadgeLabel ?? "Đặt online nhanh");
    }

    private static IReadOnlyList<ServiceShiftOptionDto> BuildServiceShifts(List<BookableSlotItem> effectiveDaySlots)
    {
        var morningSlots = effectiveDaySlots.Where(s => s.Shift == "MORNING").ToList();
        var afternoonSlots = effectiveDaySlots.Where(s => s.Shift == "AFTERNOON").ToList();

        return new[]
        {
            BuildSingleServiceShift(morningSlots, isMorning: true),
            BuildSingleServiceShift(afternoonSlots, isMorning: false)
        };
    }

    private static ServiceShiftOptionDto BuildSingleServiceShift(
        List<BookableSlotItem> shiftSlots,
        bool isMorning)
    {
        var id = isMorning ? "shift-morning" : "shift-afternoon";
        var shift = isMorning ? "MORNING" : "AFTERNOON";
        var title = isMorning ? "Ca Khám Buổi Sáng" : "Ca Khám Buổi Chiều";
        var description = isMorning ? MorningShiftDescription : AfternoonShiftDescription;

        var rc = shiftSlots.Sum(s => s.Remaining);

        string timeRange;
        string checkInWindow;

        if (shiftSlots.Count > 0)
        {
            var minStart = shiftSlots.Min(s => s.StartsAt);
            var maxEnd = shiftSlots.Max(s => s.EndsAt);
            var maxStart = shiftSlots.Max(s => s.StartsAt);

            timeRange = $"{minStart:HH:mm} - {maxEnd:HH:mm}";
            checkInWindow = $"Giờ tiếp đón tiêu chuẩn: {minStart:HH:mm} - {maxStart:HH:mm}";
        }
        else
        {
            timeRange = isMorning ? "07:30 - 11:30" : "13:30 - 16:30";
            checkInWindow = "Không mở tiếp đón";
        }

        string status;
        string badgeText;

        if (shiftSlots.Count == 0)
        {
            status = "FULL";
            badgeText = "Không mở lịch";
        }
        else if (rc == 0)
        {
            status = "FULL";
            badgeText = "Hết lượt khám";
        }
        else if (rc <= FewSlotsThreshold)
        {
            status = "FEW_SLOTS";
            badgeText = $"Còn {rc} lượt khám";
        }
        else
        {
            status = "AVAILABLE";
            badgeText = $"Còn {rc} lượt khám";
        }

        return new ServiceShiftOptionDto(
            Id: id,
            Shift: shift,
            Title: title,
            TimeRange: timeRange,
            CheckInWindow: checkInWindow,
            RemainingCapacity: rc,
            BadgeText: badgeText,
            Status: status,
            Description: description);
    }

    private static string FormatDayTopLabel(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "Thứ 2",
        DayOfWeek.Tuesday => "Thứ 3",
        DayOfWeek.Wednesday => "Thứ 4",
        DayOfWeek.Thursday => "Thứ 5",
        DayOfWeek.Friday => "Thứ 6",
        DayOfWeek.Saturday => "Thứ 7",
        _ => "Chủ Nhật"
    };

    internal static string FormatFullWeekday(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "Thứ Hai",
        DayOfWeek.Tuesday => "Thứ Ba",
        DayOfWeek.Wednesday => "Thứ Tư",
        DayOfWeek.Thursday => "Thứ Năm",
        DayOfWeek.Friday => "Thứ Sáu",
        DayOfWeek.Saturday => "Thứ Bảy",
        _ => "Chủ Nhật"
    };

    private sealed record BookableSlotItem(
        long Id,
        DateTime StartsAt,
        DateTime EndsAt,
        int Remaining,
        string Shift);
}
