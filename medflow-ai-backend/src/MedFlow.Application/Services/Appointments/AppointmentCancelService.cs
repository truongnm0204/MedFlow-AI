using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentCancelService : IAppointmentCancelService
{
    private const int MinReasonLength = 10;
    private const int MaxReasonLength = 250;
    private const string AuditAction = "APPOINTMENT_CANCEL";

    private readonly IApplicationDbContext _db;
    private readonly IAppointmentPolicyService _policyService;
    private readonly IAppointmentBookingLock _bookingLock;
    private readonly IAuditLogger _audit;
    private readonly IAppointmentDetailService _detailService;
    private readonly TimeProvider _timeProvider;

    public AppointmentCancelService(
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

    public async Task<AppointmentDetailDto> CancelMyAppointmentAsync(
        long userId,
        string? idOrCode,
        CancelAppointmentRequest? request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");

        var (trimmedIdentifier, appointmentId, appointmentCode) = AppointmentReadModel.ParseIdentifier(idOrCode);

        var reason = request?.CancellationReason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length < MinReasonLength)
            throw new ValidationException("Vui lòng nhập lý do hủy lịch tối thiểu 10 ký tự.");

        if (reason.Length > MaxReasonLength)
            throw new ValidationException("Lý do hủy lịch không được vượt quá 250 ký tự.");

        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var owned = await AppointmentBookingRules.LoadOwnedPatientsAsync(_db, userId, nowLocal, ct);
        var scope = AppointmentReadModel.BuildScopeMembers(owned.Self, owned.FamilyLinks, today);

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
            throw new NotFoundException("Appointment", trimmedIdentifier);

        if (!appointment.PatientId.HasValue || !scope.Any(m => m.Patient.Id == appointment.PatientId.Value))
            throw new ForbiddenException("Bạn không có quyền hủy lịch hẹn này.");

        if (appointment.Slot is null || appointment.SlotId is null || appointment.Slot.StartsAt is null || appointment.Slot.EndsAt is null)
            throw new NotFoundException("Appointment", trimmedIdentifier);

        await using var tx = await _db.BeginTransactionAsync(ct);
        await _bookingLock.AcquireAsync(appointment.PatientId.Value, null, appointment.SlotId.Value, ct);

        var tracked = await _db.Appointments.FirstAsync(a => a.Id == appointment.Id, ct);
        var visit = await _db.Visits
            .AsNoTracking()
            .Where(v => v.AppointmentId == appointment.Id)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);

        var status = AppointmentReadModel.ResolveEffectiveStatus(tracked, visit);

        if (status == "CANCELLED")
            throw new ConflictException("Lịch hẹn đã được hủy trước đó.");

        if (!AppointmentReadModel.IsModifiableStatus(status))
            throw new ConflictException("Lịch hẹn ở trạng thái hiện tại không thể hủy.");

        var hospitalId = appointment.Slot.Service?.HospitalId ?? appointment.Slot.Doctor?.HospitalId ?? 0L;
        var policy = await _policyService.GetEffectiveAsync(hospitalId, ct);

        if (!AppointmentReadModel.IsWithinCancelWindow(appointment.Slot.StartsAt.Value, nowLocal, policy.CancelMinimumHours))
            throw new ValidationException($"Chỉ được hủy lịch trước giờ khám tối thiểu {policy.CancelMinimumHours} giờ.");

        tracked.Status = AppointmentBookingRules.CancelledStatus;
        tracked.CancellationReason = reason;
        tracked.CancelledAt = nowLocal;
        tracked.CancelledByUserId = userId;

        var appointmentIdStr = tracked.Id.ToString(CultureInfo.InvariantCulture);

        _audit.Add(
            userId,
            AuditAction,
            "APPOINTMENT",
            appointmentIdStr,
            new
            {
                tracked.AppointmentCode,
                PatientId = tracked.PatientId,
                SlotId = tracked.SlotId,
                PreviousStatus = status,
                NewStatus = AppointmentBookingRules.CancelledStatus
            },
            ipAddress);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await _detailService.GetMyAppointmentDetailAsync(userId, appointmentIdStr, ct);
    }
}
