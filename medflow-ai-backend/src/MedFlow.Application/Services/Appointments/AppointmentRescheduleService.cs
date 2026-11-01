using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentRescheduleService : IAppointmentRescheduleService
{
    private const int MaxRescheduleNoteLength = 250;
    private const string RescheduleAuditAction = "APPOINTMENT_RESCHEDULE";

    private readonly IApplicationDbContext _db;
    private readonly IAppointmentPolicyService _policyService;
    private readonly IAppointmentBookingLock _bookingLock;
    private readonly IAuditLogger _audit;
    private readonly IAppointmentDetailService _detailService;
    private readonly TimeProvider _timeProvider;

    public AppointmentRescheduleService(
        IApplicationDbContext db,
        IAppointmentPolicyService policyService,
        IAppointmentBookingLock bookingLock,
        IAuditLogger audit,
        IAppointmentDetailService detailService,
        TimeProvider timeProvider)
    {
        _db = db;
        _policyService = policyService;
        _bookingLock = bookingLock;
        _audit = audit;
        _detailService = detailService;
        _timeProvider = timeProvider;
    }

    public async Task<AppointmentDetailDto> RescheduleAsync(
        long userId,
        string? idOrCode,
        RescheduleAppointmentRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        ArgumentNullException.ThrowIfNull(request);

        var (trimmed, appointmentId, appointmentCode) =
            AppointmentBookingRules.ParseAppointmentIdentifier(idOrCode);

        if (string.IsNullOrWhiteSpace(request.TargetSlotId) ||
            !long.TryParse(request.TargetSlotId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetSlotId) ||
            targetSlotId <= 0)
        {
            throw new ValidationException("Vui lòng chọn khung giờ mới hợp lệ để đổi lịch.");
        }

        var note = string.IsNullOrWhiteSpace(request.RescheduleNote)
            ? null
            : request.RescheduleNote.Trim();

        if (note is not null && note.Length > MaxRescheduleNoteLength)
            throw new ValidationException("Ghi chú đổi lịch tối đa 250 ký tự.");

        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var query = _db.Appointments
            .AsNoTracking()
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Service)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor);

        var appointment = appointmentId.HasValue
            ? await query.FirstOrDefaultAsync(a => a.Id == appointmentId.Value, ct)
            : await query.FirstOrDefaultAsync(
                a => a.AppointmentCode != null && a.AppointmentCode.ToUpper() == appointmentCode,
                ct);

        if (appointment is null)
            throw new NotFoundException("Appointment", trimmed);

        var owned = await AppointmentBookingRules.LoadOwnedPatientsAsync(_db, userId, nowLocal, ct);
        var isOwned = appointment.PatientId.HasValue &&
            (appointment.PatientId.Value == owned.Self.Id ||
             owned.FamilyLinks.Any(l => l.MemberPatientId == appointment.PatientId.Value));

        if (!isOwned)
            throw new ForbiddenException("Bạn không có quyền đổi lịch hẹn này.");

        if (appointment.Slot is null || appointment.Slot.StartsAt is null || appointment.Slot.EndsAt is null)
            throw new NotFoundException("Appointment", trimmed);

        var current = appointment.Slot;

        var visit = await _db.Visits
            .AsNoTracking()
            .Where(v => v.AppointmentId == appointment.Id)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);

        var status = AppointmentReadModel.ResolveEffectiveStatus(appointment, visit);
        if (status is not ("BOOKED" or "CONFIRMED" or "PENDING") ||
            current.StartsAt!.Value <= nowLocal)
        {
            throw new ConflictException("Lịch hẹn ở trạng thái hiện tại không thể đổi lịch.");
        }

        var hospitalId = current.Service?.HospitalId ?? current.Doctor?.HospitalId ?? 0L;
        var policy = await _policyService.GetEffectiveAsync(hospitalId, ct);

        if (current.StartsAt.Value < nowLocal.AddHours(policy.RescheduleMinimumHours))
            throw new ValidationException($"Chỉ được đổi lịch trước giờ khám tối thiểu {policy.RescheduleMinimumHours} giờ.");

        if (current.Service?.AllowsOnlineBooking != true)
            throw new ValidationException("Dịch vụ khám này không hỗ trợ đặt lịch trực tuyến.");

        if (targetSlotId == current.Id)
            throw new ValidationException("Khung giờ mới trùng với khung giờ hiện tại của lịch hẹn.");

        var target = await _db.ExaminationSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == targetSlotId, ct)
            ?? throw new NotFoundException("ExaminationSlot", targetSlotId);

        if (target.ServiceId != current.ServiceId ||
            (current.DoctorId.HasValue && target.DoctorId != current.DoctorId))
        {
            throw new ValidationException("Khung giờ mới phải cùng dịch vụ và bác sĩ với lịch hẹn hiện tại.");
        }

        AppointmentBookingRules.EnsureSlotBookable(target, nowLocal, today, policy);

        await using var tx = await _db.BeginTransactionAsync(ct);
        await _bookingLock.AcquireAsync(appointment.PatientId!.Value, target.DoctorId, target.Id, ct);

        var tracked = await _db.Appointments.FirstAsync(a => a.Id == appointment.Id, ct);

        var lockedStatus = AppointmentReadModel.ResolveEffectiveStatus(tracked, visit);
        if (lockedStatus is not ("BOOKED" or "CONFIRMED" or "PENDING"))
            throw new ConflictException("Lịch hẹn ở trạng thái hiện tại không thể đổi lịch.");

        var lockedTarget = await _db.ExaminationSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == target.Id, ct)
            ?? throw new NotFoundException("ExaminationSlot", target.Id);

        AppointmentBookingRules.EnsureSlotBookable(lockedTarget, nowLocal, today, policy);
        await AppointmentBookingRules.EnsureSlotHasCapacityAsync(_db, lockedTarget, ct);
        await AppointmentBookingRules.EnsurePatientLimitsAsync(
            _db, tracked.PatientId!.Value, lockedTarget, nowLocal, policy, tracked.Id, ct);

        tracked.Status = AppointmentBookingRules.CancelledStatus;

        var newAppointment = new Appointment
        {
            PatientId = tracked.PatientId,
            SlotId = lockedTarget.Id,
            BookingChannel = AppointmentBookingRules.OnlineChannel,
            Status = AppointmentBookingRules.BookedStatus,
            BookedByUserId = userId,
            BookedAt = nowLocal,
            ReasonForVisit = tracked.ReasonForVisit,
            IdempotencyKey = null,
            ReceptionNote = null
        };

        _db.Appointments.Add(newAppointment);
        await _db.SaveChangesAsync(ct);

        newAppointment.AppointmentCode = $"APT-{nowLocal:yyyyMM}-{newAppointment.Id:D4}";

        _audit.Add(
            userId,
            RescheduleAuditAction,
            "APPOINTMENT",
            tracked.Id.ToString(CultureInfo.InvariantCulture),
            new
            {
                FromAppointmentId = tracked.Id,
                FromAppointmentCode = tracked.AppointmentCode,
                FromSlotId = current.Id,
                ToAppointmentId = newAppointment.Id,
                ToAppointmentCode = newAppointment.AppointmentCode,
                ToSlotId = lockedTarget.Id,
                PatientId = tracked.PatientId,
                RescheduleNote = note
            },
            ipAddress);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await _detailService.GetMyAppointmentDetailAsync(
            userId,
            newAppointment.Id.ToString(CultureInfo.InvariantCulture),
            ct);
    }
}
