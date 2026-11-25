using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Notifications;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentRescheduleService : IAppointmentRescheduleService
{
    private const int MaxRescheduleNoteLength = 250;
    internal const string RescheduleAuditAction = "APPOINTMENT_RESCHEDULE";

    private readonly IApplicationDbContext _db;
    private readonly IAppointmentPolicyService _policyService;
    private readonly IAppointmentBookingLock _bookingLock;
    private readonly IAuditLogger _audit;
    private readonly IAppointmentDetailService _detailService;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;

    public AppointmentRescheduleService(
        IApplicationDbContext db,
        IAppointmentPolicyService policyService,
        IAppointmentBookingLock bookingLock,
        IAuditLogger audit,
        IAppointmentDetailService detailService,
        TimeProvider timeProvider,
        IRealtimeNotificationPublisher? realtimePublisher = null)
    {
        _db = db;
        _policyService = policyService;
        _bookingLock = bookingLock;
        _audit = audit;
        _detailService = detailService;
        _timeProvider = timeProvider;
        _realtimePublisher = realtimePublisher ?? MedFlow.Application.Services.Realtime.NullRealtimeNotificationPublisher.Instance;
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
                .ThenInclude(s => s!.Doctor)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room);

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

        if (appointment.Slot is null || appointment.SlotId is null || appointment.Slot.StartsAt is null || appointment.Slot.EndsAt is null)
            throw new NotFoundException("Appointment", trimmed);

        var current = appointment.Slot;
        var patientId = appointment.PatientId!.Value;

        var preLockVisits = await _db.Visits
            .AsNoTracking()
            .Where(v => v.AppointmentId == appointment.Id)
            .OrderByDescending(v => v.Id)
            .ToListAsync(ct);

        var hasNonCancelledVisitPreLock = preLockVisits.Any(v =>
            v.Status == null || !string.Equals(v.Status.Trim(), AppointmentBookingRules.CancelledStatus, StringComparison.OrdinalIgnoreCase));

        var hasActiveQueuePreLock = await _db.QueueEntries
            .AsNoTracking()
            .AnyAsync(
                q => q.AppointmentId == appointment.Id &&
                     (q.VisitId != null ||
                      (q.Status != null && AppointmentBookingService.ServingQueueStatuses.Contains(q.Status.ToUpper()))),
                ct);

        var status = AppointmentReadModel.ResolveEffectiveStatus(appointment, preLockVisits.FirstOrDefault());
        if (hasNonCancelledVisitPreLock ||
            hasActiveQueuePreLock ||
            status is not ("BOOKED" or "CONFIRMED" or "PENDING") ||
            current.StartsAt!.Value <= nowLocal)
        {
            throw new ConflictException("Lịch hẹn ở trạng thái hiện tại không thể đổi lịch.");
        }

        var hospitalId = current.Service?.HospitalId
            ?? current.Room?.HospitalId
            ?? current.Doctor?.HospitalId
            ?? 0L;
        var policy = await _policyService.GetEffectiveAsync(hospitalId, ct);

        if (current.StartsAt.Value < nowLocal.AddHours(policy.RescheduleMinimumHours))
            throw new ValidationException($"Chỉ được đổi lịch trước giờ khám tối thiểu {policy.RescheduleMinimumHours} giờ.");

        if (current.Service?.AllowsOnlineBooking != true)
            throw new ValidationException("Dịch vụ khám này không hỗ trợ đặt lịch trực tuyến.");

        if (targetSlotId == current.Id)
            throw new ValidationException("Khung giờ mới trùng với khung giờ hiện tại của lịch hẹn.");

        var (preLockRootId, preLockRescheduleCount) = await ResolveLineageRescheduleStateAsync(_db, appointment, ct);
        if (preLockRescheduleCount >= policy.MaxReschedules)
        {
            throw new ConflictException(
                $"Lịch hẹn đã đạt giới hạn tối đa {policy.MaxReschedules} lần đổi lịch theo chính sách bệnh viện.");
        }

        var target = await AppointmentBookingService.IncludeResourceGraph(_db.ExaminationSlots.AsNoTracking())
            .FirstOrDefaultAsync(s => s.Id == targetSlotId, ct)
            ?? throw new NotFoundException("ExaminationSlot", targetSlotId);

        if (target.ServiceId != current.ServiceId ||
            (current.DoctorId.HasValue && target.DoctorId != current.DoctorId))
        {
            throw new ValidationException("Khung giờ mới phải cùng dịch vụ và bác sĩ với lịch hẹn hiện tại.");
        }

        var isDoctorPinned = current.DoctorId.HasValue;
        List<ExaminationSlot> preLockTargetCandidates;

        if (isDoctorPinned)
        {
            AppointmentBookingRules.EnsureSlotBookable(target, nowLocal, today, policy);
            var activeCheck = await AppointmentBookingService.FilterSlotsByActiveResourcesAsync(
                _db,
                [target],
                requireDoctorAuthorization: true,
                ct);
            if (activeCheck.Count == 0)
            {
                throw new ConflictException(AppointmentBookingService.InactiveResourceConflictMessage);
            }

            preLockTargetCandidates = [target];
        }
        else
        {
            if (target.StartsAt is null || target.EndsAt is null)
                throw new ConflictException("Khung giờ khám không còn mở để đặt lịch.");
            if (target.StartsAt.Value <= nowLocal)
                throw new ValidationException(AppointmentBookingRules.BookingSlotStartedMessage);
            if (target.StartsAt.Value < nowLocal.AddHours(policy.BookingLeadHours))
                throw new ValidationException($"Chỉ được đặt lịch trước giờ khám tối thiểu {policy.BookingLeadHours} giờ.");
            if (DateOnly.FromDateTime(target.StartsAt.Value) > today.AddDays(policy.BookingWindowDays))
                throw new ValidationException($"Chỉ được đặt lịch trong vòng {policy.BookingWindowDays} ngày tới.");

            var rawParallelSlots = await AppointmentBookingService.IncludeResourceGraph(_db.ExaminationSlots.AsNoTracking())
                .Where(s =>
                    s.Id != current.Id &&
                    s.ServiceId == current.ServiceId &&
                    s.Status.ToUpper() == "OPEN" &&
                    s.StartsAt == target.StartsAt &&
                    s.EndsAt == target.EndsAt)
                .OrderBy(s => s.Id == targetSlotId ? 0 : 1)
                .ThenBy(s => s.Id)
                .ToListAsync(ct);

            if (rawParallelSlots.Count == 0)
                throw new ConflictException("Khung giờ khám không còn mở để đặt lịch.");

            preLockTargetCandidates = await AppointmentBookingService.FilterSlotsByActiveResourcesAsync(
                _db,
                rawParallelSlots,
                requireDoctorAuthorization: false,
                ct);

            if (preLockTargetCandidates.Count == 0)
                throw new ConflictException(AppointmentBookingService.InactiveResourceConflictMessage);
        }

        await using var tx = await AppointmentBookingService.BeginTransactionSafeAsync(_db, ct);

        Appointment newAppointment;
        ExaminationSlot lockedTarget;
        ExaminationSlot lockedCurrentSlot;

        try
        {
            var doctorIdsToLock = preLockTargetCandidates
                .Select(s => s.DoctorId)
                .Append(current.DoctorId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            var roomIdsToLock = preLockTargetCandidates
                .Select(s => s.RoomId)
                .Append(current.RoomId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            var slotIdsToLock = preLockTargetCandidates
                .Select(s => s.Id)
                .Append(current.Id)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            // ADR-0059 strict lock order: patients (02) -> services (04) -> doctors (05, ASC) -> hospital_rooms (06, ASC) -> examination_slots (07, ASC) -> appointments (08, ASC)
            await _bookingLock.AcquireBatchAsync(
                patientId,
                current.ServiceId,
                doctorIdsToLock,
                roomIdsToLock,
                slotIdsToLock,
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
                ?? throw new NotFoundException("Appointment", trimmed);

            if (tracked.PatientId != patientId || tracked.SlotId != current.Id)
            {
                throw new ConflictException("Lịch hẹn đã được thay đổi bởi thao tác đồng thời. Vui lòng tải lại trước khi đổi lịch.");
            }

            var lockedVisits = await _db.Visits
                .AsNoTracking()
                .Where(v => v.AppointmentId == tracked.Id)
                .OrderByDescending(v => v.Id)
                .ToListAsync(ct);

            var hasNonCancelledVisit = lockedVisits.Any(v =>
                v.Status == null || !string.Equals(v.Status.Trim(), AppointmentBookingRules.CancelledStatus, StringComparison.OrdinalIgnoreCase));

            var hasActiveQueue = await _db.QueueEntries
                .AsNoTracking()
                .AnyAsync(
                    q => q.AppointmentId == tracked.Id &&
                         (q.VisitId != null ||
                          (q.Status != null && AppointmentBookingService.ServingQueueStatuses.Contains(q.Status.ToUpper()))),
                    ct);

            var lockedStatus = AppointmentReadModel.ResolveEffectiveStatus(tracked, lockedVisits.FirstOrDefault());
            if (hasNonCancelledVisit ||
                hasActiveQueue ||
                lockedStatus is not ("BOOKED" or "CONFIRMED" or "PENDING"))
            {
                throw new ConflictException("Lịch hẹn ở trạng thái hiện tại không thể đổi lịch.");
            }

            lockedCurrentSlot = await _db.ExaminationSlots
                .AsNoTracking()
                .Include(s => s.Service)
                .Include(s => s.Room)
                .Include(s => s.Doctor)
                .FirstOrDefaultAsync(s => s.Id == current.Id, ct)
                ?? throw new NotFoundException("ExaminationSlot", current.Id);

            if (lockedCurrentSlot.StartsAt is null || lockedCurrentSlot.StartsAt.Value <= nowLocal)
            {
                throw new ConflictException("Lịch hẹn ở trạng thái hiện tại không thể đổi lịch.");
            }

            if (lockedCurrentSlot.StartsAt.Value < nowLocal.AddHours(policy.RescheduleMinimumHours))
            {
                throw new ValidationException($"Chỉ được đổi lịch trước giờ khám tối thiểu {policy.RescheduleMinimumHours} giờ.");
            }

            var (lockedRootId, lockedRescheduleCount) = await ResolveLineageRescheduleStateAsync(_db, tracked, ct);
            if (lockedRescheduleCount >= policy.MaxReschedules)
            {
                throw new ConflictException(
                    $"Lịch hẹn đã đạt giới hạn tối đa {policy.MaxReschedules} lần đổi lịch theo chính sách bệnh viện.");
            }

            if (isDoctorPinned)
            {
                lockedTarget = await AppointmentBookingService.IncludeResourceGraph(_db.ExaminationSlots.AsNoTracking())
                    .FirstOrDefaultAsync(s => s.Id == target.Id, ct)
                    ?? throw new NotFoundException("ExaminationSlot", target.Id);

                if (lockedTarget.ServiceId != current.ServiceId || lockedTarget.DoctorId != current.DoctorId)
                {
                    throw new ConflictException("Khung giờ mới không còn khớp với bác sĩ hoặc dịch vụ của lịch hẹn.");
                }

                AppointmentBookingRules.EnsureSlotBookable(lockedTarget, nowLocal, today, policy);
                var activeTargetCheck = await AppointmentBookingService.FilterSlotsByActiveResourcesAsync(
                    _db,
                    [lockedTarget],
                    requireDoctorAuthorization: true,
                    ct);
                if (activeTargetCheck.Count == 0)
                {
                    throw new ConflictException(AppointmentBookingService.InactiveResourceConflictMessage);
                }

                await AppointmentBookingRules.EnsureSlotHasCapacityAsync(_db, lockedTarget, ct);
            }
            else
            {
                var candidateIds = preLockTargetCandidates.Select(s => s.Id).ToList();
                var reloadedTargets = await AppointmentBookingService.IncludeResourceGraph(_db.ExaminationSlots.AsNoTracking())
                    .Where(s => candidateIds.Contains(s.Id))
                    .ToListAsync(ct);

                var openBookableTargets = reloadedTargets
                    .Where(s =>
                        s.Id != current.Id &&
                        s.ServiceId == current.ServiceId &&
                        string.Equals(s.Status, "OPEN", StringComparison.OrdinalIgnoreCase) &&
                        s.StartsAt == target.StartsAt &&
                        s.EndsAt == target.EndsAt &&
                        s.StartsAt.HasValue &&
                        s.StartsAt.Value > nowLocal &&
                        s.StartsAt.Value >= nowLocal.AddHours(policy.BookingLeadHours) &&
                        DateOnly.FromDateTime(s.StartsAt.Value) <= today.AddDays(policy.BookingWindowDays))
                    .ToList();

                if (openBookableTargets.Count == 0)
                {
                    throw new ConflictException("Khung giờ khám không còn mở để đặt lịch.");
                }

                var activeTargets = await AppointmentBookingService.FilterSlotsByActiveResourcesAsync(
                    _db,
                    openBookableTargets,
                    requireDoctorAuthorization: false,
                    ct);

                if (activeTargets.Count == 0)
                {
                    throw new ConflictException(AppointmentBookingService.InactiveResourceConflictMessage);
                }

                var activeTargetIds = activeTargets.Select(s => s.Id).ToList();
                var bookedCounts = await _db.Appointments
                    .AsNoTracking()
                    .Where(a =>
                        a.SlotId != null &&
                        activeTargetIds.Contains((long)a.SlotId) &&
                        (a.Status == null || a.Status.ToUpper() != AppointmentBookingRules.CancelledStatus))
                    .GroupBy(a => a.SlotId!.Value)
                    .Select(g => new { SlotId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.SlotId, x => x.Count, ct);

                lockedTarget = activeTargets
                        .OrderBy(s => s.Id == targetSlotId ? 0 : 1)
                        .ThenBy(s => s.Id)
                        .FirstOrDefault(s => bookedCounts.GetValueOrDefault(s.Id, 0) < (s.Capacity ?? 0))
                    ?? throw new ConflictException("Khung giờ khám vừa hết chỗ. Vui lòng chọn khung giờ khác.");
            }

            await AppointmentBookingRules.EnsurePatientLimitsAsync(
                _db,
                tracked.PatientId!.Value,
                lockedTarget,
                nowLocal,
                policy,
                tracked.Id,
                ct);

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

            var cancellationReason = string.IsNullOrWhiteSpace(note)
                ? "Đổi sang khung giờ khám mới."
                : $"Đổi lịch khám: {note}";

            tracked.Status = AppointmentBookingRules.CancelledStatus;
            tracked.CancelledAt = nowLocal;
            tracked.CancelledByUserId = userId;
            tracked.CancellationReason = cancellationReason;

            var replacementRootId = tracked.RootAppointmentId ?? lockedRootId;
            newAppointment = new Appointment
            {
                PatientId = tracked.PatientId,
                SlotId = lockedTarget.Id,
                BookingChannel = AppointmentBookingRules.OnlineChannel,
                Status = AppointmentBookingRules.BookedStatus,
                BookedByUserId = userId,
                BookedAt = nowLocal,
                ReasonForVisit = tracked.ReasonForVisit,
                IdempotencyKey = null,
                ReceptionNote = null,
                RootAppointmentId = replacementRootId,
                RescheduledFromAppointmentId = tracked.Id,
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
                    FromSlotId = lockedCurrentSlot.Id,
                    ToAppointmentId = newAppointment.Id,
                    ToAppointmentCode = newAppointment.AppointmentCode,
                    ToSlotId = lockedTarget.Id,
                    RootAppointmentId = replacementRootId,
                    RescheduleSequence = lockedRescheduleCount + 1,
                    PatientId = tracked.PatientId,
                    RescheduleNote = note
                },
                ipAddress);

            await AppointmentNotificationComposer.InvalidatePendingRemindersAsync(
                _db,
                tracked.Id,
                "Hủy gửi nhắc lịch do bệnh nhân đã đổi sang khung giờ khám mới.",
                ct);

            await AppointmentNotificationComposer.CancelReservedQueueEntriesForAppointmentAsync(
                _db,
                tracked.Id,
                actorUserId: userId,
                hospitalId: hospitalId,
                reason: "Hủy lượt xếp hàng đặt trước do bệnh nhân đổi lịch khám.",
                ipAddress: ipAddress,
                nowUtc: _timeProvider.GetUtcNow().UtcDateTime,
                ct: ct);

            var authorizedEmail = await AppointmentNotificationComposer.ResolveAuthorizedRecipientEmailAsync(
                _db,
                tracked.PatientId!.Value,
                userId,
                nowLocal,
                ct);
            var targetRoomLabel = lockedTarget.RoomId is long targetRoomId
                ? await _db.HospitalRooms
                    .AsNoTracking()
                    .Where(r => r.Id == targetRoomId)
                    .Select(r => !string.IsNullOrWhiteSpace(r.Name) ? r.Name : r.RoomCode)
                    .FirstOrDefaultAsync(ct)
                : null;
            _db.Notifications.Add(AppointmentNotificationComposer.CreateRescheduledNotification(
                patientId: tracked.PatientId.Value,
                newAppointmentId: newAppointment.Id,
                oldAppointmentCode: tracked.AppointmentCode ?? $"APT-{tracked.Id}",
                newAppointmentCode: newAppointment.AppointmentCode,
                serviceName: lockedCurrentSlot.Service?.Name ?? current.Service?.Name ?? "Khám ngoại trú",
                newStartsAt: lockedTarget.StartsAt!.Value,
                newEndsAt: lockedTarget.EndsAt!.Value,
                roomLabel: targetRoomLabel,
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
            throw new ConflictException("Lịch hẹn hoặc khung giờ khám đang được thao tác đồng thời. Vui lòng tải lại và thử lại.");
        }

        var touchesToday =
            (lockedCurrentSlot.StartsAt.HasValue && DateOnly.FromDateTime(lockedCurrentSlot.StartsAt.Value) == today) ||
            (lockedTarget.StartsAt.HasValue && DateOnly.FromDateTime(lockedTarget.StartsAt.Value) == today);

        if (touchesToday && hospitalId > 0)
        {
            await MedFlow.Application.Services.Realtime.RealtimePublishSafeExtensions.TryPublishQueueChangedSafeAsync(
                _realtimePublisher,
                new MedFlow.Application.DTOs.Realtime.QueueChangedNotification(
                    HospitalId: hospitalId,
                    ServicePointId: null,
                    RoomId: lockedTarget.RoomId ?? lockedCurrentSlot.RoomId,
                    AppointmentId: newAppointment.Id),
                ct);
        }

        return await _detailService.GetMyAppointmentDetailAsync(
            userId,
            newAppointment.Id.ToString(CultureInfo.InvariantCulture),
            ct);
    }

    internal static async Task<(long RootAppointmentId, int RescheduleCount)> ResolveLineageRescheduleStateAsync(
        IApplicationDbContext db,
        Appointment appointment,
        CancellationToken ct)
    {
        if (!appointment.PatientId.HasValue)
        {
            return (appointment.RootAppointmentId ?? appointment.Id, 0);
        }

        var patientId = appointment.PatientId.Value;
        var patientAppointments = await db.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .Select(a => new
            {
                a.Id,
                a.RootAppointmentId,
                a.RescheduledFromAppointmentId,
            })
            .ToListAsync(ct);

        var knownIds = patientAppointments.Select(a => a.Id).ToHashSet();
        knownIds.Add(appointment.Id);

        var parentByChild = new Dictionary<long, long>();
        var childrenByParent = new Dictionary<long, HashSet<long>>();

        void AddEdge(long fromId, long toId)
        {
            if (fromId <= 0 || toId <= 0 || fromId == toId)
            {
                return;
            }

            if (!parentByChild.ContainsKey(toId))
            {
                parentByChild[toId] = fromId;
            }

            if (!childrenByParent.TryGetValue(fromId, out var set))
            {
                set = new HashSet<long>();
                childrenByParent[fromId] = set;
            }

            set.Add(toId);
        }

        foreach (var row in patientAppointments)
        {
            if (row.RescheduledFromAppointmentId is long fromId)
            {
                AddEdge(fromId, row.Id);
            }

            if (row.RootAppointmentId is long rootId && rootId != row.Id)
            {
                AddEdge(rootId, row.Id);
            }
        }

        // Pre-023 fallback: reconstruct reschedule chain from APPOINTMENT_RESCHEDULE audit logs when RootAppointmentId was null.
        if (knownIds.Count > 0)
        {
            try
            {
                var idStrings = knownIds
                    .Select(id => id.ToString(CultureInfo.InvariantCulture))
                    .ToList();

                var auditRows = await db.AuditLogs
                    .AsNoTracking()
                    .Where(l =>
                        l.Action == RescheduleAuditAction &&
                        l.EntityId != null &&
                        idStrings.Contains(l.EntityId))
                    .Select(l => new { l.EntityId, l.ChangeData })
                    .ToListAsync(ct);

                foreach (var log in auditRows)
                {
                    if (!TryParseRescheduleAuditEdge(log.EntityId, log.ChangeData, out var fromId, out var toId))
                    {
                        continue;
                    }

                    if (knownIds.Contains(fromId) && knownIds.Contains(toId))
                    {
                        AddEdge(fromId, toId);
                    }
                }
            }
            catch (NotSupportedException)
            {
                // Minimal unit test contexts without AuditLogs configured.
            }
        }

        var rootCursor = appointment.RootAppointmentId is long explicitRoot && knownIds.Contains(explicitRoot)
            ? explicitRoot
            : appointment.Id;

        var ancestorGuard = new HashSet<long> { rootCursor };
        while (parentByChild.TryGetValue(rootCursor, out var parentId) &&
               knownIds.Contains(parentId) &&
               ancestorGuard.Add(parentId))
        {
            rootCursor = parentId;
        }

        var lineageNodes = new HashSet<long> { rootCursor };
        var queue = new Queue<long>();
        queue.Enqueue(rootCursor);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var childId in children)
            {
                if (knownIds.Contains(childId) && lineageNodes.Add(childId))
                {
                    queue.Enqueue(childId);
                }
            }
        }

        return (rootCursor, Math.Max(0, lineageNodes.Count - 1));
    }

    private static bool TryParseRescheduleAuditEdge(
        string? entityId,
        string? changeData,
        out long fromId,
        out long toId)
    {
        fromId = 0;
        toId = 0;

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            long.TryParse(entityId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out fromId);
        }

        if (string.IsNullOrWhiteSpace(changeData))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(changeData);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (fromId <= 0 &&
                    string.Equals(prop.Name, "FromAppointmentId", StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.TryGetInt64(out var parsedFrom))
                {
                    fromId = parsedFrom;
                }
                else if (string.Equals(prop.Name, "ToAppointmentId", StringComparison.OrdinalIgnoreCase) &&
                         prop.Value.TryGetInt64(out var parsedTo))
                {
                    toId = parsedTo;
                }
            }

            return fromId > 0 && toId > 0 && fromId != toId;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
