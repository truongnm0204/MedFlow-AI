using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.DTOs.Services;
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
        var selection = await ResolveSelectionAsync(userId, request, ct);

        await using var tx = await _db.BeginTransactionAsync(ct);
        await _bookingLock.AcquireAsync(selection.PatientId, selection.Slot.DoctorId, selection.Slot.Id, ct);

        var slot = await _db.ExaminationSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == selection.Slot.Id, ct)
            ?? throw new NotFoundException("ExaminationSlot", selection.Slot.Id);

        AppointmentBookingRules.EnsureSlotBookable(slot, selection.NowLocal, selection.Today, selection.Policy);
        await AppointmentBookingRules.EnsureSlotHasCapacityAsync(_db, slot, ct);
        await AppointmentBookingRules.EnsurePatientLimitsAsync(_db, selection.PatientId, slot, selection.NowLocal, selection.Policy, null, ct);

        var appointment = new Appointment
        {
            PatientId = selection.PatientId,
            SlotId = slot.Id,
            BookingChannel = AppointmentBookingRules.OnlineChannel,
            Status = AppointmentBookingRules.BookedStatus,
            BookedByUserId = userId,
            BookedAt = selection.NowLocal,
            ReasonForVisit = selection.SymptomNote,
            IdempotencyKey = null,
            ReceptionNote = null
        };

        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync(ct);

        appointment.AppointmentCode = $"APT-{selection.NowLocal:yyyyMM}-{appointment.Id:D4}";
        var appointmentIdStr = appointment.Id.ToString(CultureInfo.InvariantCulture);

        _audit.Add(
            userId,
            "APPOINTMENT_BOOK",
            "APPOINTMENT",
            appointmentIdStr,
            new { appointment.AppointmentCode, SlotId = slot.Id, PatientId = selection.PatientId },
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

    public async Task<AppointmentReviewPreviewResponse> PreviewAsync(
        long userId,
        GetAppointmentReviewPreviewRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isServicePreview = string.Equals(request.Mode?.Trim(), "service", StringComparison.OrdinalIgnoreCase);
        string? slotOrShiftId;
        if (isServicePreview)
        {
            var hasNumericSlotId = !string.IsNullOrWhiteSpace(request.SlotId) &&
                                   long.TryParse(request.SlotId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSlotId) &&
                                   parsedSlotId > 0;
            var hasLegacyShiftId = !string.IsNullOrWhiteSpace(request.ShiftId) &&
                                   (string.Equals(request.ShiftId.Trim(), "shift-morning", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(request.ShiftId.Trim(), "shift-afternoon", StringComparison.OrdinalIgnoreCase));
            slotOrShiftId = hasNumericSlotId && !hasLegacyShiftId
                ? request.SlotId
                : (!string.IsNullOrWhiteSpace(request.ShiftId) ? request.ShiftId : request.SlotId);
        }
        else
        {
            slotOrShiftId = request.SlotId;
        }
        var selection = await ResolveSelectionAsync(
            userId,
            new CreateAppointmentRequest
            {
                Mode = request.Mode,
                PatientId = request.PatientId,
                DoctorId = request.DoctorId,
                ServiceId = request.ServiceId,
                ServiceCode = request.ServiceCode,
                DateIso = request.DateIso,
                SlotOrShiftId = slotOrShiftId,
                SymptomNote = request.SymptomNote
            },
            ct);

        await AppointmentBookingRules.EnsureSlotHasCapacityAsync(_db, selection.Slot, ct);
        await AppointmentBookingRules.EnsurePatientLimitsAsync(_db, selection.PatientId, selection.Slot, selection.NowLocal, selection.Policy, null, ct);

        var doctor = selection.Doctor ?? (selection.Slot.DoctorId is long id
            ? await _doctorCatalog.GetByIdOrCodeAsync(id.ToString(CultureInfo.InvariantCulture), ct)
            : null);

        var room = selection.Slot.RoomId is long roomId
            ? await _db.HospitalRooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct)
            : null;

        var isSelf = selection.Owned.Self.Id == selection.PatientId;
        Patient patient;
        string? relationship = null;

        if (isSelf)
        {
            patient = selection.Owned.Self;
        }
        else
        {
            var link = selection.Owned.FamilyLinks.First(l => l.MemberPatientId == selection.PatientId);
            patient = link.MemberPatient!;
            relationship = link.Relationship;
        }

        return AppointmentReviewPreviewMapper.Map(
            patient,
            relationship,
            isSelf,
            selection.Owned.Self,
            selection.SymptomNote,
            selection.IsDoctorMode,
            selection.IsMorningShift,
            selection.Service,
            doctor,
            selection.Slot,
            room,
            selection.Policy,
            selection.Today,
            selection.HasExplicitSlotId);
    }

    private async Task<BookingSelection> ResolveSelectionAsync(
        long userId,
        CreateAppointmentRequest request,
        CancellationToken ct)
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

        if (request.SymptomNote is not null && (symptomNote is null || symptomNote.Length < 3))
            throw new ValidationException("Vui lòng nhập lý do khám hoặc triệu chứng ban đầu (tối thiểu 3 ký tự).");

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
            var shiftOrSlot = request.SlotOrShiftId?.Trim();
            isMorningShift = string.Equals(shiftOrSlot, "shift-morning", StringComparison.OrdinalIgnoreCase);
            var isAfternoonShift = string.Equals(shiftOrSlot, "shift-afternoon", StringComparison.OrdinalIgnoreCase);

            if (!isMorningShift && !isAfternoonShift)
            {
                if (string.IsNullOrWhiteSpace(shiftOrSlot) ||
                    !long.TryParse(shiftOrSlot, NumberStyles.Integer, CultureInfo.InvariantCulture, out requestedSlotId) ||
                    requestedSlotId <= 0)
                {
                    throw new ValidationException("Khung giờ khám không hợp lệ.");
                }
            }
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
        DoctorDetailDto? doctorDto = null;

        if (isDoctorMode)
        {
            doctorDto = await _doctorCatalog.GetByIdOrCodeAsync(request.DoctorId!, ct);
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

            AppointmentBookingRules.EnsureSlotBookable(candidate, nowLocal, today, policy);
            isMorningShift = candidate.StartsAt.Value.TimeOfDay < AppointmentBookingRules.AfternoonShiftStart;
            preselectedSlot = candidate;
        }
        else if (requestedSlotId > 0)
        {
            var candidate = await _db.ExaminationSlots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == requestedSlotId, ct)
                ?? throw new NotFoundException("ExaminationSlot", requestedSlotId);

            if (candidate.ServiceId != serviceId ||
                candidate.StartsAt is null ||
                DateOnly.FromDateTime(candidate.StartsAt.Value) != dateIso)
            {
                throw new ValidationException("Khung giờ khám không khớp với bác sĩ, dịch vụ hoặc ngày đã chọn.");
            }

            AppointmentBookingRules.EnsureSlotBookable(candidate, nowLocal, today, policy);
            isMorningShift = candidate.StartsAt.Value.TimeOfDay < AppointmentBookingRules.AfternoonShiftStart;
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

        return new BookingSelection(
            IsDoctorMode: isDoctorMode,
            IsMorningShift: isMorningShift,
            HasExplicitSlotId: isDoctorMode || requestedSlotId > 0,
            PatientId: patientId,
            SymptomNote: symptomNote,
            Service: serviceDto,
            Doctor: doctorDto,
            Owned: owned,
            Policy: policy,
            Slot: preselectedSlot,
            NowLocal: nowLocal,
            Today: today);
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

    private sealed record BookingSelection(
        bool IsDoctorMode,
        bool IsMorningShift,
        bool HasExplicitSlotId,
        long PatientId,
        string? SymptomNote,
        ExaminationServiceDto Service,
        DoctorDetailDto? Doctor,
        AppointmentBookingRules.OwnedPatients Owned,
        AppointmentPolicyDto Policy,
        ExaminationSlot Slot,
        DateTime NowLocal,
        DateOnly Today);
}
