using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentBookingService : IAppointmentBookingService
{
    private const int MaxSymptomNoteLength = 500;

    private readonly IApplicationDbContext _db;
    private readonly IDoctorCatalogService _doctorCatalog;
    private readonly IExaminationServiceCatalogService _serviceCatalog;
    private readonly IAppointmentPolicyService _policyService;
    private readonly IAppointmentBookingLock _bookingLock;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _timeProvider;

    public AppointmentBookingService(
        IApplicationDbContext db,
        IDoctorCatalogService doctorCatalog,
        IExaminationServiceCatalogService serviceCatalog,
        IAppointmentPolicyService policyService,
        IAppointmentBookingLock bookingLock,
        IAuditLogger audit,
        TimeProvider timeProvider)
    {
        _db = db;
        _doctorCatalog = doctorCatalog;
        _serviceCatalog = serviceCatalog;
        _policyService = policyService;
        _bookingLock = bookingLock;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<CreateAppointmentResponse> CreateAsync(
        long userId,
        CreateAppointmentRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        ArgumentNullException.ThrowIfNull(request);

        var mode = request.Mode?.Trim();
        var isDoctorMode = string.Equals(mode, "doctor", StringComparison.OrdinalIgnoreCase);
        var isServiceMode = string.Equals(mode, "service", StringComparison.OrdinalIgnoreCase);
        if (!isDoctorMode && !isServiceMode)
            throw new ValidationException("Hình thức đặt lịch không hợp lệ.");

        if (string.IsNullOrWhiteSpace(request.PatientId) ||
            !long.TryParse(request.PatientId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var patientId) ||
            patientId <= 0)
        {
            throw new ValidationException("Vui lòng chọn hồ sơ người bệnh thăm khám.");
        }

        if (string.IsNullOrWhiteSpace(request.ServiceId))
            throw new ValidationException("Vui lòng chọn dịch vụ khám.");

        if (string.IsNullOrWhiteSpace(request.DateIso) ||
            !DateOnly.TryParseExact(request.DateIso.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateIso))
        {
            throw new ValidationException("Ngày khám không hợp lệ (định dạng yyyy-MM-dd).");
        }

        var symptomNote = string.IsNullOrWhiteSpace(request.SymptomNote)
            ? null
            : request.SymptomNote.Trim();

        if (symptomNote is not null && symptomNote.Length > MaxSymptomNoteLength)
            throw new ValidationException("Lý do khám tối đa 500 ký tự.");

        long requestedSlotId = 0;
        var isMorningShift = false;

        if (isDoctorMode)
        {
            if (string.IsNullOrWhiteSpace(request.DoctorId))
                throw new ValidationException("Vui lòng chọn bác sĩ.");

            if (string.IsNullOrWhiteSpace(request.SlotOrShiftId) ||
                !long.TryParse(request.SlotOrShiftId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out requestedSlotId) ||
                requestedSlotId <= 0)
            {
                throw new ValidationException("Khung giờ khám không hợp lệ.");
            }
        }
        else
        {
            var shiftId = request.SlotOrShiftId?.Trim();
            isMorningShift = string.Equals(shiftId, "shift-morning", StringComparison.OrdinalIgnoreCase);
            var isAfternoonShift = string.Equals(shiftId, "shift-afternoon", StringComparison.OrdinalIgnoreCase);

            if (!isMorningShift && !isAfternoonShift)
                throw new ValidationException("Khung giờ khám không hợp lệ.");
        }

        var serviceDto = await _serviceCatalog.GetByIdOrCodeAsync(request.ServiceId, ct);
        if (!string.IsNullOrWhiteSpace(request.ServiceCode) &&
            !string.Equals(request.ServiceCode.Trim(), serviceDto.ServiceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Mã dịch vụ khám không khớp với dịch vụ đã chọn.");
        }

        if (!serviceDto.IsOnlineBookingActive)
            throw new ValidationException("Dịch vụ khám này không hỗ trợ đặt lịch trực tuyến.");

        var serviceId = long.Parse(serviceDto.Id, CultureInfo.InvariantCulture);
        long? doctorId = null;

        if (isDoctorMode)
        {
            var doctorDto = await _doctorCatalog.GetByIdOrCodeAsync(request.DoctorId!, ct);
            var resolvedDoctorId = long.Parse(doctorDto.Id, CultureInfo.InvariantCulture);
            doctorId = resolvedDoctorId;

            var isAuthorized = await _db.DoctorServiceAuthorizations
                .AsNoTracking()
                .AnyAsync(a => a.DoctorId == resolvedDoctorId && a.ServiceId == serviceId && a.IsActive == true, ct);

            if (!isAuthorized)
                throw new ValidationException("Bác sĩ không thực hiện dịch vụ khám này.");
        }

        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var owned = await AppointmentBookingRules.LoadOwnedPatientsAsync(_db, userId, nowLocal, ct);
        var isOwned = owned.Self.Id == patientId ||
                      owned.FamilyLinks.Any(l => l.MemberPatientId == patientId);

        if (!isOwned)
            throw new ForbiddenException("Bạn không có quyền đặt lịch khám cho hồ sơ không thuộc quyền quản lý.");

        var hospitalId = await AppointmentBookingRules.ResolveHospitalIdAsync(_db, serviceId, doctorId, ct);
        var policy = await _policyService.GetEffectiveAsync(hospitalId, ct);

        ExaminationSlot preselectedSlot;
        if (isDoctorMode)
        {
            var candidate = await _db.ExaminationSlots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == requestedSlotId, ct)
                ?? throw new NotFoundException("ExaminationSlot", requestedSlotId);

            if (candidate.DoctorId != doctorId!.Value ||
                candidate.ServiceId != serviceId ||
                candidate.StartsAt is null ||
                DateOnly.FromDateTime(candidate.StartsAt.Value) != dateIso)
            {
                throw new ValidationException("Khung giờ khám không khớp với bác sĩ, dịch vụ hoặc ngày đã chọn.");
            }

            EnsureSlotBookable(candidate, nowLocal, today, policy);
            preselectedSlot = candidate;
        }
        else
        {
            preselectedSlot = await SelectServiceShiftSlotAsync(
                serviceId,
                dateIso,
                isMorningShift,
                nowLocal,
                today,
                policy,
                ct);
        }

        await using var tx = await _db.BeginTransactionAsync(ct);
        await _bookingLock.AcquireAsync(patientId, preselectedSlot.DoctorId, preselectedSlot.Id, ct);

        var slot = await _db.ExaminationSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == preselectedSlot.Id, ct)
            ?? throw new NotFoundException("ExaminationSlot", preselectedSlot.Id);

        EnsureSlotBookable(slot, nowLocal, today, policy);

        var occupying = await _db.Appointments
            .AsNoTracking()
            .CountAsync(a =>
                a.SlotId == slot.Id &&
                (a.Status == null || a.Status.ToUpper() != AppointmentBookingRules.CancelledStatus), ct);

        if (occupying >= (slot.Capacity ?? 0))
            throw new ConflictException("Khung giờ khám vừa hết chỗ. Vui lòng chọn khung giờ khác.");

        await EnsurePatientLimitsAsync(patientId, slot, nowLocal, policy, ct);

        var appointment = new Appointment
        {
            PatientId = patientId,
            SlotId = slot.Id,
            BookingChannel = AppointmentBookingRules.OnlineChannel,
            Status = AppointmentBookingRules.BookedStatus,
            BookedByUserId = userId,
            BookedAt = nowLocal,
            ReasonForVisit = symptomNote,
            IdempotencyKey = null,
            ReceptionNote = null
        };

        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync(ct);

        appointment.AppointmentCode = $"APT-{nowLocal:yyyyMM}-{appointment.Id:D4}";
        var appointmentIdStr = appointment.Id.ToString(CultureInfo.InvariantCulture);

        _audit.Add(
            userId,
            "APPOINTMENT_BOOK",
            "APPOINTMENT",
            appointmentIdStr,
            new { appointment.AppointmentCode, SlotId = slot.Id, PatientId = patientId },
            ipAddress);

        await _db.SaveChangesAsync(ct);

        var queueNumber = await AppointmentBookingRules.ComputeQueueNumberAsync(_db, appointment.Id, slot, ct);

        await tx.CommitAsync(ct);

        return new CreateAppointmentResponse(
            AppointmentId: appointmentIdStr,
            AppointmentCode: appointment.AppointmentCode,
            QueueNumber: queueNumber,
            Status: AppointmentBookingRules.BookedStatus);
    }

    private async Task<ExaminationSlot> SelectServiceShiftSlotAsync(
        long serviceId,
        DateOnly dateIso,
        bool isMorningShift,
        DateTime nowLocal,
        DateOnly today,
        AppointmentPolicyDto policy,
        CancellationToken ct)
    {
        var dayStart = dateIso.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);

        var rawSlots = await _db.ExaminationSlots
            .AsNoTracking()
            .Where(s =>
                s.ServiceId == serviceId &&
                s.Status.ToUpper() == "OPEN" &&
                s.StartsAt != null &&
                s.EndsAt != null &&
                s.StartsAt >= dayStart &&
                s.StartsAt < dayEnd)
            .OrderBy(s => s.StartsAt)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        var minStartTime = nowLocal.AddHours(policy.BookingLeadHours);
        var maxDate = today.AddDays(policy.BookingWindowDays);

        var candidates = rawSlots
            .Where(s =>
            {
                var startsAt = s.StartsAt!.Value;
                var inShift = isMorningShift
                    ? startsAt.TimeOfDay < AppointmentBookingRules.AfternoonShiftStart
                    : startsAt.TimeOfDay >= AppointmentBookingRules.AfternoonShiftStart;

                return inShift &&
                       startsAt >= minStartTime &&
                       DateOnly.FromDateTime(startsAt) <= maxDate;
            })
            .ToList();

        if (candidates.Count == 0)
            throw new ConflictException("Ca khám đã hết chỗ. Vui lòng chọn ca hoặc ngày khác.");

        var candidateIds = candidates.Select(s => s.Id).ToList();
        var bookedCounts = await _db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.SlotId != null &&
                candidateIds.Contains((long)a.SlotId) &&
                (a.Status == null || a.Status.ToUpper() != AppointmentBookingRules.CancelledStatus))
            .GroupBy(a => a.SlotId!.Value)
            .Select(g => new { SlotId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SlotId, x => x.Count, ct);

        var selected = candidates.FirstOrDefault(s =>
            (s.Capacity ?? 0) - bookedCounts.GetValueOrDefault(s.Id, 0) > 0);

        if (selected is null)
            throw new ConflictException("Ca khám đã hết chỗ. Vui lòng chọn ca hoặc ngày khác.");

        return selected;
    }

    private static void EnsureSlotBookable(
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

        if (slot.StartsAt.Value < nowLocal.AddHours(policy.BookingLeadHours))
            throw new ValidationException($"Chỉ được đặt lịch trước giờ khám tối thiểu {policy.BookingLeadHours} giờ.");

        if (DateOnly.FromDateTime(slot.StartsAt.Value) > today.AddDays(policy.BookingWindowDays))
            throw new ValidationException($"Chỉ được đặt lịch trong vòng {policy.BookingWindowDays} ngày tới.");
    }

    private async Task EnsurePatientLimitsAsync(
        long patientId,
        ExaminationSlot slot,
        DateTime nowLocal,
        AppointmentPolicyDto policy,
        CancellationToken ct)
    {
        var slotStartsAt = slot.StartsAt!.Value;
        var slotEndsAt = slot.EndsAt!.Value;

        var hasOverlap = await _db.Appointments
            .AsNoTracking()
            .AnyAsync(a =>
                a.PatientId == patientId &&
                (a.Status == null || !AppointmentBookingRules.InactiveStatuses.Contains(a.Status.ToUpper())) &&
                a.Slot != null &&
                a.Slot.StartsAt != null &&
                a.Slot.EndsAt != null &&
                a.Slot.StartsAt < slotEndsAt &&
                a.Slot.EndsAt > slotStartsAt, ct);

        if (hasOverlap)
            throw new ConflictException("Hồ sơ này đã có lịch khám trùng thời gian.");

        var activeCount = await _db.Appointments
            .AsNoTracking()
            .CountAsync(a =>
                a.PatientId == patientId &&
                (a.Status == null || !AppointmentBookingRules.InactiveStatuses.Contains(a.Status.ToUpper())) &&
                a.Slot != null &&
                a.Slot.EndsAt != null &&
                a.Slot.EndsAt > nowLocal, ct);

        if (activeCount >= policy.MaxActiveBookings)
            throw new ConflictException($"Hồ sơ này đã đạt tối đa {policy.MaxActiveBookings} lịch khám đang hoạt động.");

        if (policy.NoShowBlockDays > 0)
        {
            var lookbackStart = nowLocal.AddDays(-policy.NoShowLookbackDays);
            var noShowStarts = await _db.Appointments
                .AsNoTracking()
                .Where(a =>
                    a.PatientId == patientId &&
                    a.Status != null &&
                    a.Status.ToUpper() == AppointmentBookingRules.NoShowStatus &&
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
}
