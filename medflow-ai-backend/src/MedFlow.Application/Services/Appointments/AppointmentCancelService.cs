using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Notifications;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
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
    private readonly IRealtimeNotificationPublisher _realtimePublisher;
    private readonly IRealtimeAccessRevoker _accessRevoker;

    public AppointmentCancelService(
        IApplicationDbContext db,
        IAppointmentPolicyService policyService,
        IAppointmentBookingLock bookingLock,
        IAuditLogger audit,
        IAppointmentDetailService detailService,
        TimeProvider timeProvider,
        IRealtimeNotificationPublisher? realtimePublisher = null,
        IRealtimeAccessRevoker? accessRevoker = null)
    {
        _db = db;
        _policyService = policyService;
        _bookingLock = bookingLock;
        _audit = audit;
        _detailService = detailService;
        _timeProvider = timeProvider;
        _realtimePublisher = realtimePublisher ?? MedFlow.Application.Services.Realtime.NullRealtimeNotificationPublisher.Instance;
        _accessRevoker = accessRevoker ?? MedFlow.Application.Services.Realtime.NullRealtimeAccessRevoker.Instance;
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
                .ThenInclude(s => s!.Doctor)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room);

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

        var initialSlot = appointment.Slot;
        var patientId = appointment.PatientId.Value;
        var hospitalId = initialSlot.Service?.HospitalId
            ?? initialSlot.Room?.HospitalId
            ?? initialSlot.Doctor?.HospitalId
            ?? 0L;
        var policy = await _policyService.GetEffectiveAsync(hospitalId, ct);
        var (preLockRootId, _) = await AppointmentRescheduleService.ResolveLineageRescheduleStateAsync(_db, appointment, ct);

        await using var tx = await AppointmentBookingService.BeginTransactionSafeAsync(_db, ct);

        string appointmentIdStr;
        long? cancelledRoomId;
        long cancelledAppointmentId;

        try
        {
            var doctorIds = initialSlot.DoctorId.HasValue
                ? new[] { initialSlot.DoctorId.Value }
                : Array.Empty<long>();
            var roomIds = initialSlot.RoomId.HasValue
                ? new[] { initialSlot.RoomId.Value }
                : Array.Empty<long>();

            // ADR-0059 strict lock order: patients (02) -> services (04) -> doctors (05) -> hospital_rooms (06) -> examination_slots (07) -> appointments (08)
            await _bookingLock.AcquireBatchAsync(
                patientId,
                initialSlot.ServiceId,
                doctorIds,
                roomIds,
                [initialSlot.Id],
                ct);

            var apptIdsToLock = new[] { preLockRootId, appointment.Id }
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
            await _db.TryLockAppointmentsForUpdateAsync(apptIdsToLock, ct);

            if (AppointmentBookingService.IsRelationalDatabase(_db) && _db is DbContext efDb)
            {
                efDb.ChangeTracker.Clear();
            }

            var tracked = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == appointment.Id, ct)
                ?? throw new NotFoundException("Appointment", trimmedIdentifier);

            if (tracked.PatientId != patientId || tracked.SlotId != initialSlot.Id)
            {
                throw new ConflictException("Lịch hẹn đã được cập nhật bởi thao tác đồng thời. Vui lòng tải lại trước khi hủy.");
            }

            var lockedSlot = await _db.ExaminationSlots
                .AsNoTracking()
                .Include(s => s.Service)
                .Include(s => s.Room)
                .Include(s => s.Doctor)
                .FirstOrDefaultAsync(s => s.Id == initialSlot.Id, ct)
                ?? throw new NotFoundException("ExaminationSlot", initialSlot.Id);

            if (lockedSlot.StartsAt is null)
            {
                throw new ConflictException("Lịch hẹn ở trạng thái hiện tại không thể hủy.");
            }

            var lockedVisits = await _db.Visits
                .AsNoTracking()
                .Where(v => v.AppointmentId == tracked.Id)
                .OrderByDescending(v => v.Id)
                .ToListAsync(ct);

            var status = AppointmentReadModel.ResolveEffectiveStatus(tracked, lockedVisits.FirstOrDefault());

            if (status == "CANCELLED")
                throw new ConflictException("Lịch hẹn đã được hủy trước đó.");

            var hasNonCancelledVisit = lockedVisits.Any(v =>
                v.Status == null || !string.Equals(v.Status.Trim(), AppointmentBookingRules.CancelledStatus, StringComparison.OrdinalIgnoreCase));

            var hasActiveQueue = await _db.QueueEntries
                .AsNoTracking()
                .AnyAsync(
                    q => q.AppointmentId == tracked.Id &&
                         (q.VisitId != null ||
                          (q.Status != null && AppointmentBookingService.ServingQueueStatuses.Contains(q.Status.ToUpper()))),
                    ct);

            if (hasNonCancelledVisit || hasActiveQueue || !AppointmentReadModel.IsModifiableStatus(status))
                throw new ConflictException("Lịch hẹn ở trạng thái hiện tại không thể hủy.");

            if (!AppointmentReadModel.IsWithinCancelWindow(lockedSlot.StartsAt.Value, nowLocal, policy.CancelMinimumHours))
                throw new ValidationException($"Chỉ được hủy lịch trước giờ khám tối thiểu {policy.CancelMinimumHours} giờ.");

            var reservedQueueIds = await _db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    q.AppointmentId == tracked.Id &&
                    q.VisitId == null &&
                    q.Status != null &&
                    q.Status.ToUpper() == "RESERVED")
                .OrderBy(q => q.Id)
                .Select(q => q.Id)
                .ToListAsync(ct);

            if (reservedQueueIds.Count > 0)
            {
                await _db.TryLockQueueEntriesForUpdateAsync(reservedQueueIds, ct);
            }

            tracked.Status = AppointmentBookingRules.CancelledStatus;
            tracked.CancellationReason = reason;
            tracked.CancelledAt = nowLocal;
            tracked.CancelledByUserId = userId;

            cancelledAppointmentId = tracked.Id;
            cancelledRoomId = lockedSlot.RoomId;
            appointmentIdStr = tracked.Id.ToString(CultureInfo.InvariantCulture);

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

            await AppointmentNotificationComposer.InvalidatePendingRemindersAsync(
                _db,
                tracked.Id,
                "Hủy gửi nhắc lịch do bệnh nhân đã hủy lịch hẹn.",
                ct);

            await AppointmentNotificationComposer.CancelReservedQueueEntriesForAppointmentAsync(
                _db,
                tracked.Id,
                actorUserId: userId,
                hospitalId: hospitalId,
                reason: $"Hủy lượt xếp hàng đặt trước do bệnh nhân hủy lịch hẹn: {reason}",
                ipAddress: ipAddress,
                nowUtc: _timeProvider.GetUtcNow().UtcDateTime,
                ct: ct);

            var authorizedEmail = await AppointmentNotificationComposer.ResolveAuthorizedRecipientEmailAsync(
                _db,
                tracked.PatientId!.Value,
                userId,
                nowLocal,
                ct);
            _db.Notifications.Add(AppointmentNotificationComposer.CreateCancelledNotification(
                patientId: tracked.PatientId!.Value,
                appointmentId: tracked.Id,
                appointmentCode: tracked.AppointmentCode ?? $"APT-{tracked.Id}",
                serviceName: lockedSlot.Service?.Name ?? initialSlot.Service?.Name ?? "Khám ngoại trú",
                startsAt: lockedSlot.StartsAt.Value,
                authorizedEmail: authorizedEmail,
                nowLocal: nowLocal));

            await _db.SaveChangesAsync(ct);

            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }
        }
        catch (Exception ex) when (AppointmentBookingService.IsConcurrencyOrLockConflict(ex))
        {
            await AppointmentBookingService.RollbackAndClearTrackerAsync(_db, tx, ct);
            throw new ConflictException("Lịch hẹn đang được thao tác đồng thời. Vui lòng tải lại và thử lại.");
        }

        await MedFlow.Application.Services.Realtime.RealtimePublishSafeExtensions.TryPublishQueueChangedSafeAsync(
            _realtimePublisher,
            new MedFlow.Application.DTOs.Realtime.QueueChangedNotification(
                HospitalId: hospitalId,
                RoomId: cancelledRoomId,
                AppointmentId: cancelledAppointmentId),
            ct);
        await MedFlow.Application.Services.Realtime.RealtimeRevokeSafeExtensions.TryRevokeAppointmentTrackingSafeAsync(
            _accessRevoker,
            cancelledAppointmentId,
            ct);

        return await _detailService.GetMyAppointmentDetailAsync(userId, appointmentIdStr, ct);
    }
}
