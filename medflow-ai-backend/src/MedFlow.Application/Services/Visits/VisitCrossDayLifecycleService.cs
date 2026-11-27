using System.Globalization;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Realtime;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Application.Services.Visits;

public sealed class VisitCrossDayLifecycleService : IVisitCrossDayLifecycleService
{
    public const string VisitResumedAuditAction = "VISIT_RESUMED";
    public const string VisitClinicalReviewCoordinatedAuditAction = "VISIT_CLINICAL_REVIEW_COORDINATED";
    public const string VisitDeferredAuditAction = "VISIT_DEFERRED";
    public const string VisitDoctorAssignedAuditAction = "VISIT_DOCTOR_ASSIGNED";

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly HashSet<string> TerminalVisitStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMPLETED",
        "DONE",
        "CANCELLED",
        "NO_SHOW",
    };

    private readonly IApplicationDbContext _db;
    private readonly IVisitChargeSettlementReader _settlementReader;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;
    private readonly TimeProvider _timeProvider;

    public VisitCrossDayLifecycleService(
        IApplicationDbContext db,
        IVisitChargeSettlementReader settlementReader,
        IRealtimeNotificationPublisher? realtimePublisher = null,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _settlementReader = settlementReader;
        _realtimePublisher = realtimePublisher ?? new NullRealtimeNotificationPublisher();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<VisitResumeContextResponseDto> GetResumeContextAsync(
        long actorUserId,
        string idOrCode,
        bool allowPatientRole = true,
        CancellationToken ct = default)
    {
        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var actor = await LoadActiveActorAsync(actorUserId, nowUtc, ct);
        var visit = await ResolveVisitAsNoTrackingAsync(idOrCode, ct);
        var visitHospitalId = await ResolveSingleVisitHospitalIdAsync(visit, ct);

        await AuthorizeResumeContextReadAsync(actor, visit, visitHospitalId, allowPatientRole, ct);

        var policy = await CrossDayVisitPolicyReader.LoadAsync(_db, visitHospitalId, nowUtc, ct);

        var visitQueues = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.VisitId == visit.Id ||
                (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
            .OrderBy(q => q.QueueNumber)
            .ThenBy(q => q.Id)
            .ToListAsync(ct);

        var isPresentToday = VisitPresenceEvaluator.IsPresentToday(visit, visitQueues, todayLocal);

        var orders = await _db.DiagnosticOrders
            .AsNoTracking()
            .Where(o => o.VisitId == visit.Id)
            .OrderBy(o => o.Id)
            .ToListAsync(ct);

        var serviceIds = orders
            .Where(o => o.ServiceId.HasValue)
            .Select(o => o.ServiceId!.Value)
            .Distinct()
            .ToList();

        var servicesById = serviceIds.Count == 0
            ? new Dictionary<long, Service>()
            : (await _db.Services
                .AsNoTracking()
                .Where(s => serviceIds.Contains(s.Id))
                .ToListAsync(ct))
                .ToDictionary(s => s.Id);

        var isReadyByOrders = await DiagnosticOrderLifecycleHelper.EvaluateVisitReturnReadinessAsync(
            _db,
            visit.Id,
            ct: ct);

        var isTerminalVisit = TerminalVisitStatuses.Contains(AppointmentBookingRules.NormalizeStatusKey(visit.Status));
        var isReadyForReturn = !isTerminalVisit &&
                               (orders.Count > 0 ? isReadyByOrders : visit.ReadyForReturnAt.HasValue);

        var overdueReasons = new List<string>();
        var orderDtos = new List<VisitResumeDiagnosticOrderItemDto>(orders.Count);

        foreach (var order in orders)
        {
            var orderCode = !string.IsNullOrWhiteSpace(order.OrderCode)
                ? order.OrderCode.Trim()
                : $"CLS-{todayLocal:yyyyMMdd}-{order.Id:D4}";
            var serviceName = order.ServiceId.HasValue && servicesById.TryGetValue(order.ServiceId.Value, out var svc)
                ? svc.Name
                : $"Dịch vụ #{order.ServiceId ?? 0}";
            var selectionStatus = NormalizeSelectionStatus(order.SelectionStatus);
            var orderStatus = AppointmentBookingRules.NormalizeStatusKey(order.Status);
            var gate = await _settlementReader.CheckDiagnosticOrderEligibilityAsync(order.Id, ct);
            var isSettled = gate.IsUnlocked;
            var validityExpiresAt = CrossDayVisitPolicyReader.ComputeOrderValidityExpiresAt(
                order,
                policy.DiagnosticOrderValidityDays);
            var isExpired = CrossDayVisitPolicyReader.IsOrderExpiredForExecution(
                order,
                policy.DiagnosticOrderValidityDays,
                nowLocal);

            if (isExpired && !overdueReasons.Contains(CrossDayVisitPolicyKeys.OverdueReasonDiagnosticOrder, StringComparer.Ordinal))
            {
                overdueReasons.Add(CrossDayVisitPolicyKeys.OverdueReasonDiagnosticOrder);
            }

            var hasActiveTodayDiagQueue = order.QueueEntryId.HasValue && visitQueues.Any(q =>
                q.Id == order.QueueEntryId.Value &&
                q.QueueDate == todayLocal &&
                !q.CompletedAt.HasValue &&
                AppointmentBookingRules.NormalizeStatusKey(q.Status) is "WAITING" or "READY" or "CALLED" or "IN_PROGRESS" &&
                AppointmentBookingRules.NormalizeStatusKey(q.PresenceStatus) == "PRESENT");

            var canResumeToday = !isTerminalVisit &&
                                 selectionStatus == DiagnosticSelectionStatuses.Accepted &&
                                 orderStatus is "ORDERED" or "CONFIRMED" &&
                                 isSettled &&
                                 !isExpired &&
                                 !hasActiveTodayDiagQueue;

            orderDtos.Add(new VisitResumeDiagnosticOrderItemDto(
                OrderId: order.Id,
                OrderCode: orderCode,
                ServiceName: serviceName,
                RequiredForConclusion: order.RequiredForConclusion == true,
                SelectionStatus: selectionStatus,
                OrderStatus: orderStatus,
                IsPaymentSettled: isSettled,
                DeferredUntilDate: order.DeferredUntilDate,
                ValidityExpiresAt: validityExpiresAt,
                IsExpiredForReview: isExpired,
                CanResumeToday: canResumeToday,
                ValidityExtendedUntil: order.ValidityExtendedUntil,
                RevalidationNote: order.RevalidationNote));
        }

        if (CrossDayVisitPolicyReader.IsReturnConsultationOverdue(visit, policy.ReturnConsultationWindowDays, nowLocal))
        {
            overdueReasons.Add(CrossDayVisitPolicyKeys.OverdueReasonReturnConsultation);
        }

        var isOverdueReturnApproved = visit.AssignedDoctorId.HasValue &&
            await MedFlow.Application.Services.Consultation.OverdueReturnSnapshotHelper.IsApprovedForCurrentSnapshotAsync(
                _db,
                visit,
                visit.AssignedDoctorId.Value,
                ct);

        var (assignedDoctorName, isAssignedDoctorActive) = await ResolveAssignedDoctorInfoAsync(visit.AssignedDoctorId, ct);
        var chargesSummary = await BuildChargesSummaryAsync(visit, ct);
        var activeTodayQueues = await BuildActiveQueueBriefsAsync(visitQueues, todayLocal, ct);

        return new VisitResumeContextResponseDto(
            VisitId: visit.Id,
            VisitCode: FormatVisitCode(visit, todayLocal),
            CheckedInAt: visit.CheckedInAt,
            VisitStatus: AppointmentBookingRules.NormalizeStatusKey(visit.Status),
            PresenceState: VisitPresenceEvaluator.NormalizePresenceState(visit.PresenceState),
            LastPresentDate: visit.LastPresentDate,
            LastResumedAt: visit.LastResumedAt,
            IsPresentToday: isPresentToday,
            ReadyForReturnAt: visit.ReadyForReturnAt,
            IsReadyForReturn: isReadyForReturn,
            AssignedDoctorId: visit.AssignedDoctorId,
            AssignedDoctorName: assignedDoctorName,
            ExaminationRoomId: visit.ExaminationRoomId,
            IsOverdueForReview: overdueReasons.Count > 0,
            OverdueReviewReasons: overdueReasons,
            ChargesSummary: chargesSummary,
            DiagnosticOrders: orderDtos,
            ActiveTodayQueues: activeTodayQueues,
            IsAssignedDoctorActive: isAssignedDoctorActive,
            IsOverdueReturnApproved: isOverdueReturnApproved);
    }

    public async Task<ResumeCrossDayVisitResponseDto> ResumeVisitAsync(
        long actorUserId,
        string idOrCode,
        ResumeCrossDayVisitRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedTarget = AppointmentBookingRules.NormalizeStatusKey(request.Target);
        if (normalizedTarget is not ("DIAGNOSTIC" or "RETURN_CONSULTATION"))
        {
            throw new ValidationException("Mục tiêu tiếp tục lượt khám (Target) phải là 'DIAGNOSTIC' hoặc 'RETURN_CONSULTATION'.");
        }

        var normalizedCoordinationPurpose = string.IsNullOrWhiteSpace(request.CoordinationPurpose)
            ? (normalizedTarget == "RETURN_CONSULTATION" ? QueueCoordinationPurposes.ResultConclusion : null)
            : request.CoordinationPurpose.Trim().ToUpperInvariant();

        if (normalizedCoordinationPurpose is not null && !QueueCoordinationPurposes.IsValid(normalizedCoordinationPurpose))
        {
            throw new ValidationException(
                "Mục đích điều phối khám lại (CoordinationPurpose) chỉ chấp nhận 'RESULT_CONCLUSION' hoặc 'CLINICAL_REVIEW'.");
        }

        if (normalizedTarget == "DIAGNOSTIC" &&
            normalizedCoordinationPurpose == QueueCoordinationPurposes.ClinicalReview)
        {
            throw new ValidationException(
                "CoordinationPurpose = 'CLINICAL_REVIEW' chỉ áp dụng khi điều phối Target = 'RETURN_CONSULTATION'.");
        }

        string? normalizedIdempotencyKey = null;
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            normalizedIdempotencyKey = request.IdempotencyKey.Trim();
            if (normalizedIdempotencyKey.Length > 80 || normalizedIdempotencyKey.Any(char.IsControl))
            {
                throw new ValidationException("IdempotencyKey không hợp lệ (tối đa 80 ký tự, không chứa ký tự điều khiển).");
            }
        }

        string? normalizedNotes = null;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            normalizedNotes = request.Notes.Trim();
            if (normalizedNotes.Length > 500 || normalizedNotes.Contains('<') || normalizedNotes.Contains('>'))
            {
                throw new ValidationException("Ghi chú tiếp tục lượt khám tối đa 500 ký tự và không chứa thẻ HTML.");
            }
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var actor = await LoadActiveActorAsync(actorUserId, nowUtc, ct);
        var preVisit = await ResolveVisitAsNoTrackingAsync(idOrCode, ct);
        var visitHospitalId = await ResolveSingleVisitHospitalIdAsync(preVisit, ct);

        EnsureReceptionResumePermission(actor, visitHospitalId);

        try
        {
            return await ExecuteResumeCoreAsync(
                actor,
                preVisit.Id,
                visitHospitalId,
                normalizedTarget,
                request,
                normalizedIdempotencyKey,
                normalizedNotes,
                ipAddress,
                nowUtc,
                nowLocal,
                todayLocal,
                ct);
        }
        catch (DbUpdateException ex) when (IsQueueIdempotencyUniqueViolation(ex) && normalizedIdempotencyKey is not null)
        {
            if (_db is DbContext efContext)
            {
                efContext.ChangeTracker.Clear();
            }

            // Concurrent identical idempotency key won the insert; replay inside a fresh lock acquisition.
            return await ExecuteResumeCoreAsync(
                actor,
                preVisit.Id,
                visitHospitalId,
                normalizedTarget,
                request,
                normalizedIdempotencyKey,
                normalizedNotes,
                ipAddress,
                nowUtc,
                nowLocal,
                todayLocal,
                ct);
        }
    }

    public async Task<DeferVisitOrOrdersResponseDto> DeferVisitOrOrdersAsync(
        long actorUserId,
        string idOrCode,
        DeferVisitOrOrdersRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException("Vui lòng nhập lý do hoãn hoặc rời viện (Reason).");
        }

        var reason = request.Reason.Trim();
        if (reason.Length < 3 || reason.Length > 500 || reason.Contains('<') || reason.Contains('>'))
        {
            throw new ValidationException("Lý do hoãn/rời viện phải từ 3 đến 500 ký tự và không chứa thẻ HTML.");
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        if (request.DeferredUntilDate.HasValue && request.DeferredUntilDate.Value < todayLocal)
        {
            throw new ValidationException("Ngày hẹn quay lại dự kiến (DeferredUntilDate) không được nhỏ hơn ngày hiện tại.");
        }

        var actor = await LoadActiveActorAsync(actorUserId, nowUtc, ct);
        var preVisit = await ResolveVisitAsNoTrackingAsync(idOrCode, ct);
        var visitHospitalId = await ResolveSingleVisitHospitalIdAsync(preVisit, ct);

        EnsureDeferPermission(actor, visitHospitalId);

        IDbContextTransaction? tx = null;
        var notificationsToPublish = new List<(long HospitalId, long? ServicePointId, long? RoomId, long? AppointmentId)>();

        try
        {
            tx = await TryBeginTransactionAsync(ct);

            if (preVisit.PatientId is long deferPatientId && deferPatientId > 0)
            {
                await _db.TryLockPatientForUpdateAsync(deferPatientId, ct);
            }

            await DiagnosticOrderLifecycleHelper.AcquireVisitAndDiagnosticLocksAsync(
                _db,
                new[] { preVisit.Id },
                extraServicePointIds: null,
                extraQueueEntryIds: null,
                extraDiagnosticOrderIds: request.DiagnosticOrderIds,
                includeChargeAndPaymentLocks: false,
                ct: ct);

            if (_db is DbContext efContext)
            {
                efContext.ChangeTracker.Clear();
            }

            var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == preVisit.Id, ct)
                ?? throw new NotFoundException("Visit", idOrCode);

            var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
            if (TerminalVisitStatuses.Contains(visitStatus))
            {
                throw new ConflictException(
                    $"[VISIT_NOT_DEFERRABLE] Lượt khám '{FormatVisitCode(visit, todayLocal)}' đã ở trạng thái kết thúc '{visitStatus}', không thể ghi nhận hoãn.");
            }

            var visitQueues = await _db.QueueEntries
                .Where(q =>
                    q.VisitId == visit.Id ||
                    (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
                .OrderBy(q => q.Id)
                .ToListAsync(ct);

            var visitOrders = await _db.DiagnosticOrders
                .Where(o => o.VisitId == visit.Id)
                .OrderBy(o => o.Id)
                .ToListAsync(ct);

            var requestedOrderIds = request.DiagnosticOrderIds?
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToList() ?? [];

            if (requestedOrderIds.Count > 0)
            {
                var ownedOrderIds = visitOrders.Select(o => o.Id).ToHashSet();
                var foreignId = requestedOrderIds.FirstOrDefault(id => !ownedOrderIds.Contains(id));
                if (foreignId > 0)
                {
                    throw new ConflictException(
                        $"Phiếu chỉ định cận lâm sàng #{foreignId} không thuộc lượt khám '{FormatVisitCode(visit, todayLocal)}'.");
                }
            }

            // Race Guard (Defer vs StartDiagnosticExecution, §15.3):
            if (requestedOrderIds.Count > 0)
            {
                var targetedOrders = visitOrders.Where(o => requestedOrderIds.Contains(o.Id)).ToList();
                var targetedQueueIds = targetedOrders
                    .Where(o => o.QueueEntryId.HasValue)
                    .Select(o => o.QueueEntryId!.Value)
                    .ToHashSet();

                var anyTargetedInProgress = targetedOrders.Any(o =>
                                                AppointmentBookingRules.NormalizeStatusKey(o.Status) == "IN_PROGRESS") ||
                                            visitQueues.Any(q =>
                                                targetedQueueIds.Contains(q.Id) &&
                                                !q.CompletedAt.HasValue &&
                                                AppointmentBookingRules.NormalizeStatusKey(q.Status) == "IN_PROGRESS");

                if (anyTargetedInProgress)
                {
                    throw new ConflictException(
                        "Không thể ghi nhận hoãn/rời viện đối với dịch vụ đang trong quá trình thực hiện trực tiếp (IN_PROGRESS).");
                }

                if (request.PatientLeftHospital && HasAnyActiveInProgressQueueOrOrder(visitQueues, visitOrders))
                {
                    throw new ConflictException(
                        "Không thể ghi nhận hoãn/rời viện đối với dịch vụ đang trong quá trình thực hiện trực tiếp (IN_PROGRESS).");
                }
            }
            else if (HasAnyActiveInProgressQueueOrOrder(visitQueues, visitOrders))
            {
                throw new ConflictException(
                    "Không thể ghi nhận hoãn/rời viện đối với dịch vụ đang trong quá trình thực hiện trực tiếp (IN_PROGRESS).");
            }

            var deferredQueueIds = new List<long>();
            var updatedOrderIds = new List<long>();

            var targetOrdersToUpdate = requestedOrderIds.Count > 0
                ? visitOrders.Where(o => requestedOrderIds.Contains(o.Id)).ToList()
                : visitOrders
                    .Where(o =>
                        AppointmentBookingRules.NormalizeStatusKey(o.Status) is "ORDERED" or "CONFIRMED" &&
                        NormalizeSelectionStatus(o.SelectionStatus) == DiagnosticSelectionStatuses.Accepted)
                    .ToList();

            var targetedDiagQueueIds = targetOrdersToUpdate
                .Where(o => o.QueueEntryId.HasValue)
                .Select(o => o.QueueEntryId!.Value)
                .ToHashSet();

            foreach (var qe in visitQueues)
            {
                if (qe.CompletedAt.HasValue)
                {
                    continue;
                }

                var qStatus = AppointmentBookingRules.NormalizeStatusKey(qe.Status);
                if (qStatus is "COMPLETED" or "DONE" or "CANCELLED" or "DEFERRED" or "NO_SHOW" or "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING")
                {
                    continue;
                }

                var shouldDeferQueue = request.PatientLeftHospital ||
                                       requestedOrderIds.Count == 0 ||
                                       targetedDiagQueueIds.Contains(qe.Id);
                if (!shouldDeferQueue)
                {
                    continue;
                }

                qe.Status = "DEFERRED";
                qe.PresenceStatus = "LEFT";
                qe.CalledAt = null;
                qe.CompletedAt = nowLocal;
                qe.BlockedReason = reason;
                qe.StateVersion = (qe.StateVersion is > 0 ? qe.StateVersion.Value : 1) + 1;
                deferredQueueIds.Add(qe.Id);

                if (visitHospitalId.HasValue)
                {
                    notificationsToPublish.Add((
                        HospitalId: visitHospitalId.Value,
                        ServicePointId: qe.CurrentServicePointId ?? qe.ServicePointId,
                        RoomId: visit.ExaminationRoomId,
                        AppointmentId: visit.AppointmentId));
                }
            }

            foreach (var order in targetOrdersToUpdate)
            {
                var oStatus = AppointmentBookingRules.NormalizeStatusKey(order.Status);
                if (oStatus is "COMPLETED" or "CANCELLED" or "IN_PROGRESS")
                {
                    continue;
                }

                if (request.DeferredUntilDate.HasValue)
                {
                    order.DeferredUntilDate = request.DeferredUntilDate.Value;
                }

                updatedOrderIds.Add(order.Id);
            }

            if (request.PatientLeftHospital)
            {
                VisitPresenceEvaluator.StampDepartedOrDeferred(
                    visit,
                    todayLocal,
                    isDeferred: request.DeferredUntilDate.HasValue);

                if (AppointmentBookingRules.NormalizeStatusKey(visit.Status) == "RETURN_CONSULTATION")
                {
                    visit.Status = "AWAITING_RESULTS";
                }
            }

            var visitCode = FormatVisitCode(visit, todayLocal);

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actor.UserId,
                HospitalId = visitHospitalId,
                Action = VisitDeferredAuditAction,
                EntityType = "VISIT",
                EntityId = visitCode,
                Reason = reason,
                ChangeData = JsonSerializer.Serialize(new
                {
                    visitId = visit.Id,
                    visitCode,
                    visitStatus = visit.Status,
                    presenceState = visit.PresenceState,
                    lastPresentDate = visit.LastPresentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    deferredUntilDate = request.DeferredUntilDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    patientLeftHospital = request.PatientLeftHospital,
                    deferredQueueEntryIds = deferredQueueIds,
                    updatedDiagnosticOrderIds = updatedOrderIds,
                }, AuditJsonOptions),
                IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                OccurredAt = nowUtc,
            });

            await _db.SaveChangesAsync(ct);

            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }

            foreach (var notif in notificationsToPublish
                         .DistinctBy(n => (n.HospitalId, n.ServicePointId, n.RoomId)))
            {
                await RealtimePublishSafeExtensions.TryPublishQueueChangedSafeAsync(
                    _realtimePublisher,
                    new QueueChangedNotification(
                        HospitalId: notif.HospitalId,
                        ServicePointId: notif.ServicePointId,
                        RoomId: notif.RoomId,
                        AppointmentId: notif.AppointmentId),
                    ct);
            }

            return new DeferVisitOrOrdersResponseDto(
                VisitId: visit.Id,
                VisitCode: visitCode,
                VisitStatus: AppointmentBookingRules.NormalizeStatusKey(visit.Status),
                PresenceState: VisitPresenceEvaluator.NormalizePresenceState(visit.PresenceState),
                LastPresentDate: visit.LastPresentDate,
                ReadyForReturnAt: visit.ReadyForReturnAt,
                DeferredUntilDate: request.DeferredUntilDate,
                DeferredQueueEntryIds: deferredQueueIds,
                UpdatedDiagnosticOrderIds: updatedOrderIds);
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }
    }

    private async Task<ResumeCrossDayVisitResponseDto> ExecuteResumeCoreAsync(
        ActorContext actor,
        long visitId,
        long? visitHospitalId,
        string normalizedTarget,
        ResumeCrossDayVisitRequestDto request,
        string? normalizedIdempotencyKey,
        string? normalizedNotes,
        string? ipAddress,
        DateTime nowUtc,
        DateTime nowLocal,
        DateOnly todayLocal,
        CancellationToken ct)
    {
        IDbContextTransaction? tx = null;
        var notificationsToPublish = new List<(long HospitalId, long? ServicePointId, long? RoomId, long? AppointmentId)>();

        try
        {
            tx = await TryBeginTransactionAsync(ct);

            // Pre-collect lock IDs strictly in ADR-0060 order:
            // 05 doctors -> 06 hospital_rooms -> 09 visits -> 10 queue_service_points -> 11 queue_entries -> 12 diagnostic_orders
            var preVisit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId, ct)
                ?? throw new NotFoundException("Visit", visitId);

            var doctorIdsToLock = new SortedSet<long>();
            if (preVisit.AssignedDoctorId is > 0)
            {
                doctorIdsToLock.Add(preVisit.AssignedDoctorId.Value);
            }

            if (request.ReassignDoctorId is > 0)
            {
                doctorIdsToLock.Add(request.ReassignDoctorId.Value);
            }

            var hospitalRooms = visitHospitalId.HasValue
                ? await _db.HospitalRooms
                    .AsNoTracking()
                    .Where(r => r.HospitalId == visitHospitalId.Value)
                    .OrderBy(r => r.Id)
                    .ToListAsync(ct)
                : [];

            var hospitalServicePoints = visitHospitalId.HasValue
                ? await _db.QueueServicePoints
                    .AsNoTracking()
                    .Where(sp => sp.HospitalId == visitHospitalId.Value)
                    .OrderBy(sp => sp.Id)
                    .ToListAsync(ct)
                : [];

            if (request.TargetServicePointId is > 0 &&
                hospitalServicePoints.All(sp => sp.Id != request.TargetServicePointId.Value))
            {
                var explicitSp = await _db.QueueServicePoints
                    .AsNoTracking()
                    .FirstOrDefaultAsync(sp => sp.Id == request.TargetServicePointId.Value, ct)
                    ?? throw new NotFoundException("QueueServicePoint", request.TargetServicePointId.Value);
                if (visitHospitalId.HasValue && explicitSp.HospitalId.HasValue && explicitSp.HospitalId.Value != visitHospitalId.Value)
                {
                    throw new ForbiddenException("Điểm phục vụ được chọn thuộc cơ sở y tế khác.");
                }

                hospitalServicePoints.Add(explicitSp);
            }

            foreach (var sp in hospitalServicePoints)
            {
                if (sp.DoctorId is > 0 && request.ReassignDoctorId == sp.DoctorId)
                {
                    doctorIdsToLock.Add(sp.DoctorId.Value);
                }
            }

            var roomIdsToLock = new SortedSet<long>();
            if (preVisit.ExaminationRoomId is > 0)
            {
                roomIdsToLock.Add(preVisit.ExaminationRoomId.Value);
            }

            foreach (var r in hospitalRooms)
            {
                roomIdsToLock.Add(r.Id);
            }

            var preOrderServiceIds = await _db.DiagnosticOrders
                .AsNoTracking()
                .Where(o => o.VisitId == visitId && o.ServiceId.HasValue)
                .Select(o => o.ServiceId!.Value)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync(ct);

            // Level 04: services (ADR-0059 order, before doctors 05 and rooms 06)
            foreach (var sid in preOrderServiceIds)
            {
                if (sid > 0)
                {
                    await _db.TryLockServiceForUpdateAsync(sid, ct);
                }
            }

            // Level 05: doctors
            if (doctorIdsToLock.Count > 0)
            {
                await _db.TryLockDoctorsForUpdateAsync(doctorIdsToLock.ToArray(), ct);
            }

            // Level 06: hospital_rooms
            if (roomIdsToLock.Count > 0)
            {
                await _db.TryLockRoomsForUpdateAsync(roomIdsToLock.ToArray(), ct);
            }

            // Levels 09 -> 10 -> 11 -> 12
            var spIdsToLock = hospitalServicePoints.Select(sp => sp.Id).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
            // Stage E2 / E-BR10 / QE-07..08: patient lock (level 02) precedes every visit /
            // service-point / queue lock in the canonical order. The resume path mints a
            // RETURN_CONSULTATION ticket below — serialize on the patient row so a concurrent
            // queue-creating writer for the same patient cannot race a second active ticket.
            if (preVisit.PatientId is > 0)
            {
                await _db.TryLockPatientForUpdateAsync(preVisit.PatientId.Value, ct);
            }
            await DiagnosticOrderLifecycleHelper.AcquireVisitAndDiagnosticLocksAsync(
                _db,
                new[] { visitId },
                extraServicePointIds: spIdsToLock,
                extraQueueEntryIds: null,
                extraDiagnosticOrderIds: null,
                includeChargeAndPaymentLocks: false,
                ct: ct);

            if (_db is DbContext efContext &&
                !string.Equals(efContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal))
            {
                efContext.ChangeTracker.Clear();
            }

            var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, ct)
                ?? throw new NotFoundException("Visit", visitId);

            var visitCode = FormatVisitCode(visit, todayLocal);
            var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
            if (TerminalVisitStatuses.Contains(visitStatus))
            {
                throw new ConflictException(
                    $"[VISIT_NOT_RESUMABLE] Lượt khám '{visitCode}' đã ở trạng thái kết thúc '{visitStatus}', không thể tiếp tục (Resume).");
            }

            var effectiveHospitalId = visitHospitalId ?? actor.Doctor?.HospitalId ?? actor.Staff?.HospitalId ?? 0;
            var policy = await CrossDayVisitPolicyReader.LoadAsync(_db, visitHospitalId, nowUtc, ct);
            if (!policy.AllowCrossDayVisit &&
                visit.CheckedInAt.HasValue &&
                DateOnly.FromDateTime(visit.CheckedInAt.Value) < todayLocal)
            {
                throw new ConflictException(
                    "[CROSS_DAY_VISIT_DISABLED] Chính sách cơ sở y tế hiện không cho phép tiếp tục lượt khám qua ngày.");
            }

            var visitQueues = await _db.QueueEntries
                .Where(q =>
                    q.VisitId == visit.Id ||
                    (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
                .OrderBy(q => q.Id)
                .ToListAsync(ct);

            var visitOrders = await _db.DiagnosticOrders
                .Where(o => o.VisitId == visit.Id)
                .OrderBy(o => o.Id)
                .ToListAsync(ct);

            var orderServiceIds = visitOrders
                .Where(o => o.ServiceId.HasValue)
                .Select(o => o.ServiceId!.Value)
                .Distinct()
                .ToList();
            var servicesById = orderServiceIds.Count == 0
                ? new Dictionary<long, Service>()
                : (await _db.Services
                    .AsNoTracking()
                    .Where(s => orderServiceIds.Contains(s.Id))
                    .ToListAsync(ct))
                    .ToDictionary(s => s.Id);

            // Reload rooms, departments, and service points post-lock so concurrent Admin deactivations are strictly enforced
            hospitalRooms = await _db.HospitalRooms
                .AsNoTracking()
                .Where(r => r.HospitalId == effectiveHospitalId)
                .OrderBy(r => r.Id)
                .ToListAsync(ct);
            var roomsById = hospitalRooms.ToDictionary(r => r.Id);

            var departmentsById = (await _db.Departments
                .AsNoTracking()
                .Where(d => d.HospitalId == effectiveHospitalId)
                .ToListAsync(ct))
                .ToDictionary(d => d.Id);

            hospitalServicePoints = await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp => sp.HospitalId == effectiveHospitalId)
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);
            var normalizedCoordinationPurpose = string.IsNullOrWhiteSpace(request.CoordinationPurpose)
                ? (normalizedTarget == "RETURN_CONSULTATION" ? QueueCoordinationPurposes.ResultConclusion : null)
                : request.CoordinationPurpose.Trim().ToUpperInvariant();

            // Prior-day rollover predicate (decision doc §4.1): ONLY unstarted tickets (WAITING / READY)
            // may be deferred. Any active execution (IN_PROGRESS / IN_CONSULTATION / SERVING) is never
            // auto-deferred regardless of QueueDate or queue type. Clinical serving sessions fail closed
            // with CLINICAL_HANDOFF_REQUIRED until Clinical Session Recovery completes; CLS/Pharmacy
            // execution is a domain conflict (never aborted through the clinical recovery authority).
            var closedPriorDayCount = 0;
            foreach (var staleQueue in visitQueues.Where(q =>
                         q.QueueDate.HasValue &&
                         q.QueueDate.Value < todayLocal &&
                         !q.CompletedAt.HasValue))
            {
                var qStatus = AppointmentBookingRules.NormalizeStatusKey(staleQueue.Status);
                var qType = AppointmentBookingRules.NormalizeStatusKey(staleQueue.QueueType);
                var effectiveType = string.IsNullOrEmpty(qType)
                    ? ClinicalQueueRules.QueueTypeExamination
                    : qType;

                if (qStatus is "COMPLETED" or "DONE" or "CANCELLED" or "DEFERRED" or "NO_SHOW" or "SKIPPED" or "RESERVED")
                {
                    continue;
                }

                if (qStatus is "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING")
                {
                    if (effectiveType is ClinicalQueueRules.QueueTypeExamination or ClinicalQueueRules.QueueTypeReturnConsultation)
                    {
                        throw new ConflictException(ClinicalSessionRecoveryConstants.ClinicalHandoffRequiredMessage);
                    }

                    throw new ConflictException(
                        $"[CLINICAL_EXECUTION_IN_PROGRESS] Lượt {effectiveType} đang được thực hiện trực tiếp (IN_PROGRESS) từ ngày trước. Không tự động đóng phiên thực hiện; vui lòng hoàn tất thao tác chuyên môn hiện hành.");
                }

                // CALLED is an intermediate state: never assumed interrupted, never auto-deferred.
                if (qStatus is not ("WAITING" or "READY"))
                {
                    continue;
                }

                staleQueue.Status = "DEFERRED";
                staleQueue.PresenceStatus = "LEFT";
                staleQueue.CalledAt = null;
                staleQueue.CompletedAt = nowLocal;
                staleQueue.BlockedReason ??= "Đóng lượt hàng đợi ngày cũ (DEFERRED) khi tiếp tục lượt khám sang ngày mới.";
                staleQueue.StateVersion = (staleQueue.StateVersion is > 0 ? staleQueue.StateVersion.Value : 1) + 1;
                closedPriorDayCount++;
            }

            // Stamp durable presence today (NEVER mutating CheckedInAt!)
            var wasAlreadyPresentToday = visit.LastPresentDate == todayLocal &&
                                         VisitPresenceEvaluator.NormalizePresenceState(visit.PresenceState) == VisitPresenceStates.Present;
            visit.LastPresentDate = todayLocal;
            visit.PresenceState = VisitPresenceStates.Present;
            visit.LastResumedAt ??= nowLocal;

            var resumedEntries = new List<QueueEntry>();
            var isIdempotentReplay = true;

            if (normalizedTarget == "DIAGNOSTIC")
            {
                var targetOrderIds = new SortedSet<long>();
                if (request.DiagnosticOrderId is > 0)
                {
                    targetOrderIds.Add(request.DiagnosticOrderId.Value);
                }

                if (request.DiagnosticOrderIds is not null)
                {
                    foreach (var oid in request.DiagnosticOrderIds)
                    {
                        if (oid > 0)
                        {
                            targetOrderIds.Add(oid);
                        }
                    }
                }

                if (targetOrderIds.Count == 0)
                {
                    throw new ValidationException("Vui lòng chọn ít nhất một phiếu chỉ định cận lâm sàng để tiếp tục (DiagnosticOrderId / DiagnosticOrderIds).");
                }

                var ordersById = visitOrders.ToDictionary(o => o.Id);
                var nextNumberCacheBySpKey = new Dictionary<long, int>();

                foreach (var targetOrderId in targetOrderIds)
                {
                    if (!ordersById.TryGetValue(targetOrderId, out var order))
                    {
                        throw new ConflictException(
                            $"Phiếu chỉ định cận lâm sàng #{targetOrderId} không thuộc lượt khám '{visitCode}'.");
                    }

                    var orderCode = !string.IsNullOrWhiteSpace(order.OrderCode)
                        ? order.OrderCode.Trim()
                        : $"CLS-{todayLocal:yyyyMMdd}-{order.Id:D4}";
                    var orderStatus = AppointmentBookingRules.NormalizeStatusKey(order.Status);
                    var selectionStatus = NormalizeSelectionStatus(order.SelectionStatus);

                    if (orderStatus is "CANCELLED" or "COMPLETED")
                    {
                        throw new ConflictException(
                            $"Phiếu chỉ định cận lâm sàng '{orderCode}' đang ở trạng thái '{orderStatus}', không thể tiếp tục xếp hàng thực hiện.");
                    }

                    if (selectionStatus != DiagnosticSelectionStatuses.Accepted)
                    {
                        throw new ConflictException(
                            $"Phiếu chỉ định cận lâm sàng '{orderCode}' có trạng thái lựa chọn '{selectionStatus}' (chưa được xác nhận thực hiện hoặc cần bác sĩ xem xét).");
                    }

                    var settlementGate = await _settlementReader.CheckDiagnosticOrderEligibilityAsync(order.Id, ct);
                    if (!settlementGate.IsUnlocked)
                    {
                        throw new ConflictException(
                            $"[UNPAID_DIAGNOSTIC_FEE] Phiếu chỉ định cận lâm sàng '{orderCode}' chưa hoàn tất thanh toán ({settlementGate.StatusCode}), không thể tiếp tục thực hiện.");
                    }

                    var isExpired = CrossDayVisitPolicyReader.IsOrderExpiredForExecution(
                        order,
                        policy.DiagnosticOrderValidityDays,
                        nowLocal);
                    if (isExpired)
                    {
                        throw new ConflictException(
                            $"[DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED] Phiếu chỉ định cận lâm sàng '{orderCode}' đã quá thời hạn hiệu lực ({policy.DiagnosticOrderValidityDays} ngày). Lễ tân không được phép tự gia hạn chuyên môn; vui lòng điều phối bệnh nhân gặp bác sĩ đánh giá lại (CLINICAL_REVIEW).");
                    }

                    Service? orderService = null;
                    if (order.ServiceId.HasValue)
                    {
                        servicesById.TryGetValue(order.ServiceId.Value, out orderService);
                    }

                    var targetSpId = DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
                        order,
                        orderService,
                        request.TargetServicePointId,
                        hospitalServicePoints,
                        roomsById,
                        visitQueues,
                        effectiveHospitalId,
                        departmentsById);

                    var scopedKey = normalizedIdempotencyKey is not null
                        ? BuildScopedQueueIdempotencyKey(normalizedIdempotencyKey, $"D{order.Id}")
                        : null;

                    var existingTodayDiag = visitQueues
                        .Where(q =>
                            q.QueueDate == todayLocal &&
                            string.Equals(q.QueueType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) &&
                            AppointmentBookingRules.NormalizeStatusKey(q.Status) is not ("CANCELLED" or "COMPLETED" or "DONE" or "NO_SHOW") &&
                            (q.Id == order.QueueEntryId ||
                             (scopedKey is not null && string.Equals(q.IdempotencyKey, scopedKey, StringComparison.Ordinal))))
                        .OrderByDescending(q => q.Id)
                        .FirstOrDefault();

                    if (existingTodayDiag is not null)
                    {
                        var existingQStatus = AppointmentBookingRules.NormalizeStatusKey(existingTodayDiag.Status);
                        var existingPresence = AppointmentBookingRules.NormalizeStatusKey(existingTodayDiag.PresenceStatus);
                        var spChanged = (existingTodayDiag.CurrentServicePointId ?? existingTodayDiag.ServicePointId) != targetSpId;

                        if (existingQStatus == "DEFERRED" || existingPresence != "PRESENT" || spChanged)
                        {
                            existingTodayDiag.Status = "WAITING";
                            existingTodayDiag.PresenceStatus = "PRESENT";
                            existingTodayDiag.ServicePointId ??= targetSpId;
                            existingTodayDiag.CurrentServicePointId = targetSpId;
                            existingTodayDiag.CalledAt = null;
                            existingTodayDiag.CompletedAt = null;
                            existingTodayDiag.BlockedReason = null;
                            existingTodayDiag.ArrivedAt ??= nowLocal;
                            existingTodayDiag.ReadyAt ??= nowLocal;
                            existingTodayDiag.StateVersion = (existingTodayDiag.StateVersion is > 0 ? existingTodayDiag.StateVersion.Value : 1) + 1;
                            isIdempotentReplay = false;
                        }

                        order.QueueEntryId = existingTodayDiag.Id;
                        order.DeferredUntilDate = null;
                        resumedEntries.Add(existingTodayDiag);
                        continue;
                    }

                    isIdempotentReplay = false;
                    var spCacheKey = targetSpId;
                    if (!nextNumberCacheBySpKey.TryGetValue(spCacheKey, out var currentMax))
                    {
                        var dbMax = await _db.QueueEntries
                            .AsNoTracking()
                            .Where(q =>
                                q.QueueDate == todayLocal &&
                                (q.ServicePointId == targetSpId || q.CurrentServicePointId == targetSpId))
                            .MaxAsync(q => (int?)q.QueueNumber, ct);
                        currentMax = dbMax ?? 0;
                    }

                    var nextQueueNo = currentMax + 1;
                    nextNumberCacheBySpKey[spCacheKey] = nextQueueNo;

                    var previousOrderQueue = order.QueueEntryId.HasValue
                        ? visitQueues.FirstOrDefault(q => q.Id == order.QueueEntryId.Value)
                        : null;

                    // Stage E2 / QE-07..08: enforce one active serving queue per patient per
                    // hospital before minting the DIAGNOSTIC ticket (post patient+visit locks).
                    await PatientSingleActiveQueueGuard.EnsureNoOtherActiveServingQueueAsync(
                        _db,
                        visit.PatientId,
                        effectiveHospitalId > 0 ? effectiveHospitalId : null,
                        todayLocal,
                        excludeQueueEntryId: existingTodayDiag?.Id,
                        ct: ct);
                    var newDiagQueue = new QueueEntry
                    {
                        VisitId = visit.Id,
                        AppointmentId = null,
                        ServicePointId = targetSpId,
                        CurrentServicePointId = targetSpId,
                        QueueDate = todayLocal,
                        QueueNumber = nextQueueNo,
                        QueueType = "DIAGNOSTIC",
                        Status = "WAITING",
                        ScheduledAt = null,
                        PresenceStatus = "PRESENT",
                        ArrivedAt = nowLocal,
                        ReadyAt = nowLocal,
                        ArrivalClass = "WALK_IN",
                        ManualPriority = ClinicalQueueRules.NormalizeStoredPriority(previousOrderQueue?.ManualPriority),
                        StateVersion = 1,
                        IdempotencyKey = scopedKey,
                    };

                    _db.QueueEntries.Add(newDiagQueue);
                    await _db.SaveChangesAsync(ct);

                    visitQueues.Add(newDiagQueue);
                    order.QueueEntryId = newDiagQueue.Id;
                    order.DeferredUntilDate = null;
                    resumedEntries.Add(newDiagQueue);

                    if (visitHospitalId.HasValue)
                    {
                        notificationsToPublish.Add((
                            HospitalId: visitHospitalId.Value,
                            ServicePointId: targetSpId,
                            RoomId: null,
                            AppointmentId: visit.AppointmentId));
                    }
                }

                if (visitStatus is "WAITING" or "CHECKED_IN")
                {
                    visit.Status = "AWAITING_DIAGNOSTIC";
                }
            }
            else
            {
                // Target == "RETURN_CONSULTATION"
                var effectivePurpose = normalizedCoordinationPurpose ?? QueueCoordinationPurposes.ResultConclusion;

                if (effectivePurpose == QueueCoordinationPurposes.ClinicalReview)
                {
                    var hasOrderNeedingReview = visitOrders.Any(o =>
                        AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED" &&
                        (CrossDayVisitPolicyReader.IsOrderExpiredForExecution(o, policy.DiagnosticOrderValidityDays, nowLocal) ||
                         NormalizeSelectionStatus(o.SelectionStatus) == DiagnosticSelectionStatuses.ReviewRequired));

                    if (!hasOrderNeedingReview)
                    {
                        throw new ConflictException(
                            "[CLINICAL_REVIEW_NOT_REQUIRED] Lượt khám hiện không có phiếu chỉ định cận lâm sàng quá hạn hoặc cần bác sĩ xem xét lại (REVIEW_REQUIRED).");
                    }
                }
                else
                {
                    var isReady = await DiagnosticOrderLifecycleHelper.EvaluateVisitReturnReadinessAsync(
                        _db,
                        visit.Id,
                        ct: ct);

                    if (!isReady)
                    {
                        throw new ConflictException(
                            "[RETURN_CONSULTATION_NOT_READY] Lượt khám chưa có đầy đủ kết quả cận lâm sàng bắt buộc hoặc đang còn dịch vụ cận lâm sàng đang thực hiện/chờ kết quả.");
                    }
                }

                var previousDoctorId = visit.AssignedDoctorId;
                var previousRoomId = visit.ExaminationRoomId;

                if (request.ReassignDoctorId.HasValue)
                {
                    if (!policy.AllowDoctorReassignment)
                    {
                        throw new ConflictException(
                            "[DOCTOR_REASSIGNMENT_DISABLED] Chính sách cơ sở y tế hiện không cho phép chuyển bác sĩ phụ trách khi tiếp tục lượt khám.");
                    }

                    var newDoctor = await _db.Doctors
                        .Include(d => d.Staff)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(d => d.Id == request.ReassignDoctorId.Value, ct)
                        ?? throw new NotFoundException("Doctor", request.ReassignDoctorId.Value);

                    var newDoctorHospitalId = newDoctor.HospitalId ?? newDoctor.Staff?.HospitalId;
                    if (visitHospitalId.HasValue && newDoctorHospitalId != visitHospitalId.Value)
                    {
                        throw new ForbiddenException("Bác sĩ được chọn để phân công lại thuộc cơ sở y tế khác.");
                    }

                    if (!newDoctor.IsActive ||
                        (newDoctor.Staff is not null &&
                         !string.Equals(newDoctor.Staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new ConflictException(
                            "[REASSIGN_DOCTOR_INACTIVE] Bác sĩ được chọn để tiếp nhận khám kết luận hiện đang nghỉ trực hoặc ngừng hoạt động.");
                    }

                    // Verify authorization if visit is linked to a scheduled examination service
                    if (visit.AppointmentId.HasValue)
                    {
                        var appt = await _db.Appointments.AsNoTracking()
                            .FirstOrDefaultAsync(a => a.Id == visit.AppointmentId.Value, ct);
                        var slot = appt?.SlotId.HasValue == true
                            ? await _db.ExaminationSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == appt.SlotId.Value, ct)
                            : null;
                        if (slot?.ServiceId.HasValue == true)
                        {
                            var anyAuthConfigured = await _db.DoctorServiceAuthorizations
                                .AsNoTracking()
                                .AnyAsync(dsa => dsa.ServiceId == slot.ServiceId.Value, ct);
                            if (anyAuthConfigured)
                            {
                                var isAuthorized = await _db.DoctorServiceAuthorizations
                                    .AsNoTracking()
                                    .AnyAsync(dsa =>
                                        dsa.DoctorId == newDoctor.Id &&
                                        dsa.ServiceId == slot.ServiceId.Value &&
                                        dsa.IsActive != false, ct);
                                if (!isAuthorized)
                                {
                                    throw new ConflictException(
                                        "[REASSIGN_DOCTOR_UNAUTHORIZED_SERVICE] Bác sĩ thay thế chưa được cấp quyền thực hiện dịch vụ khám chuyên khoa của lượt khám này.");
                                }
                            }
                        }
                    }

                    var newDoctorSp = ResolveExaminationServicePointForDoctor(
                        newDoctor.Id,
                        request.TargetServicePointId,
                        hospitalServicePoints,
                        hospitalRooms);
                    var newRoomId = newDoctorSp?.RoomId
                        ?? hospitalRooms.FirstOrDefault(r =>
                            string.Equals(r.RoomType?.Trim(), "EXAMINATION", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(r.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) &&
                            (!newDoctor.Staff?.DepartmentId.HasValue == true || r.DepartmentId == newDoctor.Staff?.DepartmentId))?.Id
                        ?? visit.ExaminationRoomId;

                    if (previousDoctorId != newDoctor.Id)
                    {
                        visit.OverdueReturnResultSnapshot = null;
                    }

                    visit.AssignedDoctorId = newDoctor.Id;
                    visit.ExaminationRoomId = newRoomId;

                    if (previousDoctorId != newDoctor.Id)
                    {
                        visit.OverdueReturnResultSnapshot = null;
                        isIdempotentReplay = false;
                        _db.AuditLogs.Add(new AuditLog
                        {
                            ActorUserId = actor.UserId,
                            HospitalId = visitHospitalId,
                            Action = VisitDoctorAssignedAuditAction,
                            EntityType = "VISIT",
                            EntityId = visitCode,
                            Reason = normalizedNotes ?? "Phân công lại bác sĩ khám kết luận cho lượt khám qua ngày (bác sĩ cũ vắng mặt/nghỉ trực).",
                            ChangeData = JsonSerializer.Serialize(new
                            {
                                visitId = visit.Id,
                                visitCode,
                                previousDoctorId,
                                assignedDoctorId = newDoctor.Id,
                                previousRoomId,
                                examinationRoomId = newRoomId,
                            }, AuditJsonOptions),
                            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                            OccurredAt = nowUtc,
                        });
                    }
                }

                // Validate effective assigned doctor & examination room are active
                if (visit.AssignedDoctorId.HasValue)
                {
                    var effectiveDoctor = await _db.Doctors
                        .Include(d => d.Staff)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(d => d.Id == visit.AssignedDoctorId.Value, ct);
                    if (effectiveDoctor is null ||
                        !effectiveDoctor.IsActive ||
                        (effectiveDoctor.Staff is not null &&
                         !string.Equals(effectiveDoctor.Staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new ConflictException(
                            "[ASSIGNED_DOCTOR_INACTIVE] Bác sĩ phụ trách ban đầu hiện đang nghỉ trực hoặc tạm ngừng hoạt động. Vui lòng chọn bác sĩ thay thế hợp lệ (ReassignDoctorId).");
                    }
                }

                if (visit.ExaminationRoomId.HasValue)
                {
                    var effectiveRoom = hospitalRooms.FirstOrDefault(r => r.Id == visit.ExaminationRoomId.Value)
                        ?? await _db.HospitalRooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == visit.ExaminationRoomId.Value, ct);
                    if (effectiveRoom is not null &&
                        !string.Equals(effectiveRoom.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ConflictException(
                            $"[EXAMINATION_ROOM_INACTIVE] Buồng khám '{effectiveRoom.RoomCode}' hiện đang bảo trì hoặc tạm ngừng hoạt động.");
                    }
                }

                var referenceClinical = visitQueues
                    .Where(q => ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType))
                    .OrderByDescending(q => q.Id)
                    .FirstOrDefault(q => AppointmentBookingRules.NormalizeStatusKey(q.Status) != "CANCELLED");

                var targetSpId = ResolveClinicalServicePointIdForResume(
                    visit,
                    request.TargetServicePointId,
                    referenceClinical,
                    hospitalServicePoints);

                var scopedSuffix = effectivePurpose == QueueCoordinationPurposes.ClinicalReview ? "CREV" : "RET";
                var scopedReturnKey = normalizedIdempotencyKey is not null
                    ? BuildScopedQueueIdempotencyKey(normalizedIdempotencyKey, scopedSuffix)
                    : null;

                var existingTodayReturn = visitQueues
                    .Where(q =>
                        q.QueueDate == todayLocal &&
                        ClinicalQueueRules.ResolveQueueType(q.QueueType, null) == ClinicalQueueRules.QueueTypeReturnConsultation &&
                        AppointmentBookingRules.NormalizeStatusKey(q.Status) is not ("CANCELLED" or "COMPLETED" or "DONE" or "NO_SHOW"))
                    .OrderByDescending(q => q.Id)
                    .FirstOrDefault();

                // Cross-day RETURN handoff (decision doc §4.1 / §11): the canonical prior-day rollover
                // loop above already deferred only unstarted (WAITING / READY) tickets and rejected
                // active execution. This block is a defensive fail-closed guard for a prior-day RETURN
                // ticket in an intermediate/active state (CALLED / IN_PROGRESS / IN_CONSULTATION / SERVING):
                // never auto-defer it, never treat QueueDate as evidence that the session was interrupted.
                if (existingTodayReturn is null)
                {
                    var unclosedPriorDayReturns = visitQueues
                        .Where(q =>
                            q.QueueDate.HasValue && q.QueueDate.Value < todayLocal &&
                            !q.CompletedAt.HasValue &&
                            ClinicalQueueRules.ResolveQueueType(q.QueueType, null) == ClinicalQueueRules.QueueTypeReturnConsultation &&
                            AppointmentBookingRules.NormalizeStatusKey(q.Status) is not ("CANCELLED" or "COMPLETED" or "DONE" or "NO_SHOW" or "DEFERRED" or "SKIPPED" or "RESERVED" or "WAITING" or "READY"))
                        .ToList();

                    if (unclosedPriorDayReturns.Count > 0)
                    {
                        throw new ConflictException(ClinicalSessionRecoveryConstants.ClinicalHandoffRequiredMessage);
                    }
                }

                if (effectivePurpose == QueueCoordinationPurposes.ResultConclusion)
                {
                    visit.ReadyForReturnAt ??= nowLocal;
                }

                if (existingTodayReturn is not null)
                {
                    var existingStatus = AppointmentBookingRules.NormalizeStatusKey(existingTodayReturn.Status);
                    var existingPresence = AppointmentBookingRules.NormalizeStatusKey(existingTodayReturn.PresenceStatus);
                    var spChanged = targetSpId.HasValue && existingTodayReturn.CurrentServicePointId != targetSpId.Value;
                    var purposeChanged = !string.Equals(existingTodayReturn.CoordinationPurpose, effectivePurpose, StringComparison.OrdinalIgnoreCase);

                    if (purposeChanged)
                    {
                        existingTodayReturn.CoordinationPurpose = effectivePurpose;
                        isIdempotentReplay = false;
                    }

                    if (existingStatus is "CALLED" or "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING")
                    {
                        if (existingPresence != "PRESENT")
                        {
                            existingTodayReturn.PresenceStatus = "PRESENT";
                            existingTodayReturn.StateVersion = (existingTodayReturn.StateVersion is > 0 ? existingTodayReturn.StateVersion.Value : 1) + 1;
                            isIdempotentReplay = false;
                        }
                    }
                    else if (existingStatus != "READY" || existingPresence != "PRESENT" || spChanged || existingTodayReturn.CompletedAt.HasValue)
                    {
                        existingTodayReturn.Status = "READY";
                        existingTodayReturn.PresenceStatus = "PRESENT";
                        existingTodayReturn.CoordinationPurpose = effectivePurpose;
                        existingTodayReturn.CalledAt = null;
                        existingTodayReturn.CompletedAt = null;
                        existingTodayReturn.BlockedReason = null;
                        existingTodayReturn.ArrivedAt ??= nowLocal;
                        existingTodayReturn.ReadyAt = nowLocal;
                        if (targetSpId.HasValue)
                        {
                            existingTodayReturn.ServicePointId ??= targetSpId.Value;
                            existingTodayReturn.CurrentServicePointId = targetSpId.Value;
                        }

                        existingTodayReturn.StateVersion = (existingTodayReturn.StateVersion is > 0 ? existingTodayReturn.StateVersion.Value : 1) + 1;
                        isIdempotentReplay = false;
                    }

                    if (visitStatus != "RETURN_CONSULTATION")
                    {
                        visit.Status = "RETURN_CONSULTATION";
                        isIdempotentReplay = false;
                    }

                    resumedEntries.Add(existingTodayReturn);
                }
                else
                {
                    isIdempotentReplay = false;
                    var maxToday = await _db.QueueEntries
                        .AsNoTracking()
                        .Where(q =>
                            q.QueueDate == todayLocal &&
                            (targetSpId.HasValue
                                ? (q.ServicePointId == targetSpId.Value || q.CurrentServicePointId == targetSpId.Value)
                                : (q.ServicePointId == null && q.CurrentServicePointId == null)))
                        .MaxAsync(q => (int?)q.QueueNumber, ct);

                    // Stage E2 / QE-07..08: enforce one active serving queue per patient per
                    // hospital before minting the RETURN_CONSULTATION ticket.
                    await PatientSingleActiveQueueGuard.EnsureNoOtherActiveServingQueueAsync(
                        _db,
                        visit.PatientId,
                        effectiveHospitalId > 0 ? effectiveHospitalId : null,
                        todayLocal,
                        excludeQueueEntryId: existingTodayReturn?.Id,
                        ct: ct);
                    var newReturnQueue = new QueueEntry
                    {
                        VisitId = visit.Id,
                        AppointmentId = null,
                        ServicePointId = targetSpId,
                        CurrentServicePointId = targetSpId,
                        QueueDate = todayLocal,
                        QueueNumber = (maxToday ?? 0) + 1,
                        QueueType = ClinicalQueueRules.QueueTypeReturnConsultation,
                        CoordinationPurpose = effectivePurpose,
                        Status = "READY",
                        ScheduledAt = null,
                        PresenceStatus = "PRESENT",
                        ArrivedAt = nowLocal,
                        ReadyAt = nowLocal,
                        ArrivalClass = "WALK_IN",
                        ManualPriority = ClinicalQueueRules.NormalizeStoredPriority(referenceClinical?.ManualPriority),
                        StateVersion = 1,
                        IdempotencyKey = scopedReturnKey,
                    };

                    _db.QueueEntries.Add(newReturnQueue);
                    visit.Status = "RETURN_CONSULTATION";
                    resumedEntries.Add(newReturnQueue);
                }

                if (visitHospitalId.HasValue)
                {
                    notificationsToPublish.Add((
                        HospitalId: visitHospitalId.Value,
                        ServicePointId: targetSpId,
                        RoomId: visit.ExaminationRoomId,
                        AppointmentId: visit.AppointmentId));
                }
            }

            if (!isIdempotentReplay || !wasAlreadyPresentToday || closedPriorDayCount > 0)
            {
                visit.LastResumedAt = nowLocal;

                var auditPayload = JsonSerializer.Serialize(new
                {
                    visitId = visit.Id,
                    visitCode,
                    target = normalizedTarget,
                    coordinationPurpose = normalizedCoordinationPurpose,
                    visitStatus = visit.Status,
                    presenceState = visit.PresenceState,
                    lastPresentDate = visit.LastPresentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    assignedDoctorId = visit.AssignedDoctorId,
                    examinationRoomId = visit.ExaminationRoomId,
                    closedPriorDayQueueCount = closedPriorDayCount,
                    idempotencyKey = normalizedIdempotencyKey,
                }, AuditJsonOptions);

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actor.UserId,
                    HospitalId = visitHospitalId,
                    Action = VisitResumedAuditAction,
                    EntityType = "VISIT",
                    EntityId = visitCode,
                    Reason = normalizedNotes ?? $"Tiếp tục lượt khám qua ngày ({normalizedTarget}).",
                    ChangeData = auditPayload,
                    IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                    OccurredAt = nowUtc,
                });

                if (normalizedCoordinationPurpose == QueueCoordinationPurposes.ClinicalReview)
                {
                    _db.AuditLogs.Add(new AuditLog
                    {
                        ActorUserId = actor.UserId,
                        HospitalId = visitHospitalId,
                        Action = VisitClinicalReviewCoordinatedAuditAction,
                        EntityType = "VISIT",
                        EntityId = visitCode,
                        Reason = normalizedNotes ?? "Điều phối bệnh nhân gặp bác sĩ đánh giá lại chỉ định cận lâm sàng (CLINICAL_REVIEW).",
                        ChangeData = auditPayload,
                        IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                        OccurredAt = nowUtc,
                    });
                }
            }

            await _db.SaveChangesAsync(ct);

            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }

            foreach (var notif in notificationsToPublish
                         .DistinctBy(n => (n.HospitalId, n.ServicePointId, n.RoomId)))
            {
                await RealtimePublishSafeExtensions.TryPublishQueueChangedSafeAsync(
                    _realtimePublisher,
                    new QueueChangedNotification(
                        HospitalId: notif.HospitalId,
                        ServicePointId: notif.ServicePointId,
                        RoomId: notif.RoomId,
                        AppointmentId: notif.AppointmentId),
                    ct);
            }

            var (assignedDoctorName, _) = await ResolveAssignedDoctorInfoAsync(visit.AssignedDoctorId, ct);
            var resumedBriefs = await BuildActiveQueueBriefsAsync(resumedEntries, todayLocal, ct);

            return new ResumeCrossDayVisitResponseDto(
                VisitId: visit.Id,
                VisitCode: visitCode,
                CheckedInAt: visit.CheckedInAt,
                VisitStatus: AppointmentBookingRules.NormalizeStatusKey(visit.Status),
                PresenceState: VisitPresenceEvaluator.NormalizePresenceState(visit.PresenceState),
                LastPresentDate: visit.LastPresentDate ?? todayLocal,
                LastResumedAt: visit.LastResumedAt ?? nowLocal,
                Target: normalizedTarget,
                IsIdempotentReplay: isIdempotentReplay && wasAlreadyPresentToday && closedPriorDayCount == 0,
                AssignedDoctorId: visit.AssignedDoctorId,
                AssignedDoctorName: assignedDoctorName,
                ExaminationRoomId: visit.ExaminationRoomId,
                ResumedQueues: resumedBriefs,
                ClosedPriorDayQueueCount: closedPriorDayCount,
                CoordinationPurpose: normalizedCoordinationPurpose);
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }
    }

    private static bool HasAnyActiveInProgressQueueOrOrder(
        IReadOnlyList<QueueEntry> visitQueues,
        IReadOnlyList<DiagnosticOrder> visitOrders)
    {
        if (visitOrders.Any(o =>
                AppointmentBookingRules.NormalizeStatusKey(o.Status) == "IN_PROGRESS"))
        {
            return true;
        }

        var latestNonCancelledOrderAt = visitOrders
            .Where(o => AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED" && o.OrderedAt.HasValue)
            .Select(o => o.OrderedAt!.Value)
            .DefaultIfEmpty()
            .Max();
        var hasNonCancelledOrders = latestNonCancelledOrderAt != default;

        return visitQueues.Any(q =>
        {
            if (q.CompletedAt.HasValue ||
                AppointmentBookingRules.NormalizeStatusKey(q.Status) is not ("IN_PROGRESS" or "IN_CONSULTATION" or "SERVING"))
            {
                return false;
            }

            if (string.Equals(q.QueueType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!hasNonCancelledOrders)
            {
                return true;
            }

            return q.StartedAt.HasValue && q.StartedAt.Value > latestNonCancelledOrderAt;
        });
    }

    private static string BuildScopedQueueIdempotencyKey(string baseKey, string suffix)
    {
        var combined = $"{baseKey}:{suffix}";
        return combined.Length <= 100 ? combined : combined[..100];
    }

    private static bool IsQueueIdempotencyUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner is null)
        {
            return false;
        }

        var sqlState = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
        var constraintName = inner.GetType().GetProperty("ConstraintName")?.GetValue(inner) as string;
        if (sqlState == "23505" &&
            string.Equals(constraintName, "ux_queue_entries_idempotency_key", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return inner.Message.Contains("ux_queue_entries_idempotency_key", StringComparison.OrdinalIgnoreCase);
    }

    private static long? ResolveDiagnosticServicePointIdForResume(
        DiagnosticOrder order,
        long? requestedSpId,
        IReadOnlyList<QueueEntry> visitQueues,
        IReadOnlyList<QueueServicePoint> hospitalServicePoints)
    {
        var activeDiagPoints = hospitalServicePoints
            .Where(sp =>
                sp.IsActive != false &&
                string.Equals(sp.PointType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(sp => sp.Id)
            .ToList();

        if (requestedSpId.HasValue)
        {
            var requested = activeDiagPoints.FirstOrDefault(sp => sp.Id == requestedSpId.Value);
            if (requested is null)
            {
                throw new ConflictException("Điểm phục vụ cận lâm sàng được chọn không hợp lệ hoặc đang ngừng hoạt động.");
            }

            if (requested.ServiceId.HasValue && order.ServiceId.HasValue && requested.ServiceId.Value != order.ServiceId.Value)
            {
                throw new ConflictException($"Điểm phục vụ '{requested.Name}' không tương thích với dịch vụ cận lâm sàng của phiếu chỉ định.");
            }

            return requested.Id;
        }

        if (order.QueueEntryId.HasValue)
        {
            var prevQ = visitQueues.FirstOrDefault(q => q.Id == order.QueueEntryId.Value);
            var prevSpId = prevQ?.CurrentServicePointId ?? prevQ?.ServicePointId;
            if (prevSpId.HasValue && activeDiagPoints.Any(sp => sp.Id == prevSpId.Value))
            {
                return prevSpId.Value;
            }
        }

        if (order.ServiceId.HasValue)
        {
            var exactMatch = activeDiagPoints.FirstOrDefault(sp => sp.ServiceId == order.ServiceId.Value);
            if (exactMatch is not null)
            {
                return exactMatch.Id;
            }
        }

        return activeDiagPoints.FirstOrDefault(sp => !sp.ServiceId.HasValue)?.Id
               ?? activeDiagPoints.FirstOrDefault()?.Id;
    }

    private static QueueServicePoint? ResolveExaminationServicePointForDoctor(
        long doctorId,
        long? requestedSpId,
        IReadOnlyList<QueueServicePoint> hospitalServicePoints,
        IReadOnlyList<HospitalRoom> hospitalRooms)
    {
        var activeRoomIds = hospitalRooms
            .Where(r => string.Equals(r.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Id)
            .ToHashSet();

        var activeExamPoints = hospitalServicePoints
            .Where(sp =>
                sp.IsActive != false &&
                (sp.PointType == null || string.Equals(sp.PointType.Trim(), "EXAMINATION_ROOM", StringComparison.OrdinalIgnoreCase)) &&
                (!sp.RoomId.HasValue || activeRoomIds.Count == 0 || activeRoomIds.Contains(sp.RoomId.Value)))
            .OrderBy(sp => sp.Id)
            .ToList();

        if (requestedSpId.HasValue)
        {
            return activeExamPoints.FirstOrDefault(sp => sp.Id == requestedSpId.Value);
        }

        return activeExamPoints.FirstOrDefault(sp => sp.DoctorId == doctorId);
    }

    private static long? ResolveClinicalServicePointIdForResume(
        Visit visit,
        long? requestedSpId,
        QueueEntry? referenceClinical,
        IReadOnlyList<QueueServicePoint> hospitalServicePoints)
    {
        var activeExamPoints = hospitalServicePoints
            .Where(sp =>
                sp.IsActive != false &&
                (sp.PointType == null || string.Equals(sp.PointType.Trim(), "EXAMINATION_ROOM", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(sp => sp.Id)
            .ToList();

        if (requestedSpId.HasValue)
        {
            var explicitSp = activeExamPoints.FirstOrDefault(sp => sp.Id == requestedSpId.Value);
            if (explicitSp is null)
            {
                throw new ConflictException("Điểm phục vụ buồng khám được chọn không hợp lệ hoặc đang ngừng hoạt động.");
            }

            return explicitSp.Id;
        }

        var byRoomAndDoctor = activeExamPoints.FirstOrDefault(sp =>
            visit.ExaminationRoomId.HasValue &&
            visit.AssignedDoctorId.HasValue &&
            sp.RoomId == visit.ExaminationRoomId.Value &&
            sp.DoctorId == visit.AssignedDoctorId.Value);
        if (byRoomAndDoctor is not null)
        {
            return byRoomAndDoctor.Id;
        }

        var byDoctor = activeExamPoints.FirstOrDefault(sp =>
            visit.AssignedDoctorId.HasValue && sp.DoctorId == visit.AssignedDoctorId.Value);
        if (byDoctor is not null)
        {
            return byDoctor.Id;
        }

        var byRoom = activeExamPoints.FirstOrDefault(sp =>
            visit.ExaminationRoomId.HasValue && sp.RoomId == visit.ExaminationRoomId.Value);
        if (byRoom is not null)
        {
            return byRoom.Id;
        }

        return referenceClinical?.CurrentServicePointId ?? referenceClinical?.ServicePointId;
    }

    private async Task<(string? DoctorName, bool IsActive)> ResolveAssignedDoctorInfoAsync(
        long? doctorId,
        CancellationToken ct)
    {
        if (!doctorId.HasValue)
        {
            return (null, true);
        }

        var doctor = await _db.Doctors
            .Include(d => d.Staff)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == doctorId.Value, ct);
        if (doctor is null)
        {
            return (null, false);
        }

        var isStaffActive = doctor.Staff is null ||
                            string.Equals(doctor.Staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
        var isActive = doctor.IsActive && isStaffActive;
        var title = !string.IsNullOrWhiteSpace(doctor.ProfessionalTitle) ? $"{doctor.ProfessionalTitle.Trim()} " : string.Empty;
        var fullName = doctor.Staff?.FullName?.Trim() ?? doctor.DoctorCode ?? $"BS #{doctor.Id}";
        return ($"{title}{fullName}".Trim(), isActive);
    }

    private async Task<VisitChargesBriefDto> BuildChargesSummaryAsync(Visit visit, CancellationToken ct)
    {
        try
        {
            var assessment = await VisitChargeAssessor.AssessAsync(_db, _timeProvider, visit, ct);
            return new VisitChargesBriefDto(
                PayableTotal: assessment.PayableTotal,
                RecognizedPaidTotal: assessment.RecognizedPaidTotal,
                AmountDue: assessment.AmountDue);
        }
        catch (NotSupportedException)
        {
            return new VisitChargesBriefDto(0m, 0m, 0m);
        }
    }

    private async Task<IReadOnlyList<VisitActiveQueueBriefDto>> BuildActiveQueueBriefsAsync(
        IReadOnlyList<QueueEntry> entries,
        DateOnly todayLocal,
        CancellationToken ct)
    {
        var activeToday = entries
            .Where(q =>
                q.QueueDate == todayLocal &&
                !q.CompletedAt.HasValue &&
                AppointmentBookingRules.NormalizeStatusKey(q.Status) is not ("COMPLETED" or "DONE" or "CANCELLED" or "DEFERRED" or "NO_SHOW"))
            .OrderBy(q => q.QueueNumber)
            .ThenBy(q => q.Id)
            .ToList();

        if (activeToday.Count == 0)
        {
            return [];
        }

        var spIds = activeToday
            .Select(q => q.CurrentServicePointId ?? q.ServicePointId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var spsById = new Dictionary<long, QueueServicePoint>();
        var roomsById = new Dictionary<long, HospitalRoom>();
        if (spIds.Count > 0)
        {
            try
            {
                var sps = await _db.QueueServicePoints
                    .AsNoTracking()
                    .Where(sp => spIds.Contains(sp.Id))
                    .ToListAsync(ct);
                spsById = sps.ToDictionary(sp => sp.Id);

                var roomIds = sps.Where(sp => sp.RoomId.HasValue).Select(sp => sp.RoomId!.Value).Distinct().ToList();
                if (roomIds.Count > 0)
                {
                    var rooms = await _db.HospitalRooms
                        .AsNoTracking()
                        .Where(r => roomIds.Contains(r.Id))
                        .ToListAsync(ct);
                    roomsById = rooms.ToDictionary(r => r.Id);
                }
            }
            catch (NotSupportedException)
            {
                // Minimal test DbContext.
            }
        }

        return activeToday.Select(q =>
        {
            var effSpId = q.CurrentServicePointId ?? q.ServicePointId;
            QueueServicePoint? sp = null;
            if (effSpId.HasValue)
            {
                spsById.TryGetValue(effSpId.Value, out sp);
            }

            HospitalRoom? room = null;
            if (sp?.RoomId.HasValue == true)
            {
                roomsById.TryGetValue(sp.RoomId.Value, out room);
            }

            var qNo = q.QueueNumber ?? 1;
            var roomToken = !string.IsNullOrWhiteSpace(room?.RoomCode) ? room.RoomCode.Trim() : "Q";
            var ticketNo = $"{roomToken}-{qNo:D3}";

            return new VisitActiveQueueBriefDto(
                QueueEntryId: q.Id,
                TicketNo: ticketNo,
                QueueNumber: qNo,
                QueueDate: q.QueueDate ?? todayLocal,
                QueueType: AppointmentBookingRules.NormalizeStatusKey(q.QueueType),
                Status: AppointmentBookingRules.NormalizeStatusKey(q.Status),
                PresenceStatus: AppointmentBookingRules.NormalizeStatusKey(q.PresenceStatus),
                ServicePointId: effSpId,
                ServicePointName: sp?.Name,
                RoomCode: room?.RoomCode,
                CoordinationPurpose: q.CoordinationPurpose);
        }).ToList();
    }

    private async Task<Visit> ResolveVisitAsNoTrackingAsync(string idOrCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã định danh lượt khám (idOrCode) không được để trống.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].Trim();
        }

        Visit? visit = null;
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId) && numericId > 0)
        {
            visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == numericId, ct);
        }

        if (visit is null)
        {
            var upper = trimmed.ToUpperInvariant();
            visit = await _db.Visits
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.VisitCode != null && v.VisitCode.ToUpper() == upper, ct);
        }

        return visit ?? throw new NotFoundException("Visit", idOrCode);
    }

    private async Task<long?> ResolveSingleVisitHospitalIdAsync(Visit visit, CancellationToken ct)
    {
        var map = await VisitHospitalResolver.CollectManyAsync(_db, [visit], ct);
        if (map.TryGetValue(visit.Id, out var hospitals) && hospitals.Count > 0)
        {
            if (hospitals.Count > 1)
            {
                throw new ForbiddenException("Không thể xác định cơ sở y tế duy nhất cho lượt khám này.");
            }

            return hospitals.First();
        }

        return null;
    }

    private static string FormatVisitCode(Visit visit, DateOnly fallbackDate)
        => !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode.Trim()
            : $"VIS-{fallbackDate:yyyyMM}-{visit.Id:D4}";

    private static string NormalizeSelectionStatus(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? DiagnosticSelectionStatuses.Accepted
            : raw.Trim().ToUpperInvariant();

    private static string NormalizeChargeStatus(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? VisitChargeStatuses.Active
            : raw.Trim().ToUpperInvariant();

    private async Task<IDbContextTransaction?> TryBeginTransactionAsync(CancellationToken ct)
    {
        try
        {
            return await _db.BeginTransactionAsync(ct);
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record ActorContext(
        long UserId,
        HashSet<string> RoleCodes,
        HashSet<string> PermissionCodes,
        Staff? Staff,
        Doctor? Doctor);

    private async Task<ActorContext> LoadActiveActorAsync(long actorUserId, DateTime nowUtc, CancellationToken ct)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (user.AccountStatus != AccountStatus.ACTIVE ||
            (user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc))
        {
            throw new ForbiddenException("Tài khoản người dùng hiện không ở trạng thái hoạt động hợp lệ.");
        }

        var roleIds = (await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct)).ToHashSet();

        var roles = await _db.Roles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);

        var roleSet = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);

        var permIds = (await _db.RolePermissions
            .AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .ToListAsync(ct)).ToHashSet();

        var permCodes = permIds.Count == 0
            ? []
            : await _db.Permissions
                .AsNoTracking()
                .Where(p => permIds.Contains(p.Id))
                .Select(p => p.Code)
                .ToListAsync(ct);

        var permSet = new HashSet<string>(permCodes, StringComparer.OrdinalIgnoreCase);

        var staff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        var doctor = staff is not null
            ? await _db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.StaffId == staff.Id, ct)
            : null;

        return new ActorContext(actorUserId, roleSet, permSet, staff, doctor);
    }

    private async Task AuthorizeResumeContextReadAsync(
        ActorContext actor,
        Visit visit,
        long? visitHospitalId,
        bool allowPatientRole,
        CancellationToken ct)
    {
        // SYSTEM_ADMIN is deliberately NOT a bypass: clinical/administrative scope rules apply to every actor.

        if (actor.RoleCodes.Contains(RoleNames.Reception) ||
            actor.RoleCodes.Contains(RoleNames.HospitalAdmin) ||
            actor.RoleCodes.Contains(RoleNames.Doctor) ||
            actor.RoleCodes.Contains(RoleNames.DiagnosticStaff))
        {
            var staffHospitalId = actor.Doctor?.HospitalId ?? actor.Staff?.HospitalId;
            if (actor.Staff is null ||
                !string.Equals(actor.Staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                !staffHospitalId.HasValue)
            {
                throw new ForbiddenException("Hồ sơ nhân sự không hợp lệ hoặc chưa được gán cơ sở y tế.");
            }

            if (visitHospitalId.HasValue && staffHospitalId.Value != visitHospitalId.Value)
            {
                throw new ForbiddenException("Nhân sự không có quyền xem ngữ cảnh lượt khám của cơ sở y tế khác.");
            }

            return;
        }

        if (allowPatientRole && actor.RoleCodes.Contains(RoleNames.Patient))
        {
            var ownedPatientIds = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(
                _db,
                actor.UserId,
                _timeProvider,
                ct);
            if (!visit.PatientId.HasValue || !ownedPatientIds.Contains(visit.PatientId.Value))
            {
                throw new ForbiddenException("Bệnh nhân không có quyền truy cập thông tin lượt khám của hồ sơ khác.");
            }

            return;
        }

        throw new ForbiddenException("Người dùng không có quyền truy cập ngữ cảnh tiếp tục lượt khám.");
    }

    private static void EnsureReceptionResumePermission(ActorContext actor, long? visitHospitalId)
    {
        // No SYSTEM_ADMIN bypass (DevSecOps F4): admin role never substitutes for clinical/coordination authority.
        var isReceptionOrAdmin = actor.RoleCodes.Contains(RoleNames.Reception) ||
                                 actor.RoleCodes.Contains(RoleNames.HospitalAdmin);
        if (!isReceptionOrAdmin)
        {
            throw new ForbiddenException("Chỉ nhân viên Tiếp đón hoặc Quản trị bệnh viện mới có quyền tiếp nhận tiếp tục lượt khám (Resume).");
        }

        if (actor.Staff is null ||
            !string.Equals(actor.Staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
            !actor.Staff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Hồ sơ nhân sự tiếp đón không ở trạng thái hoạt động hợp lệ.");
        }

        if (visitHospitalId.HasValue && actor.Staff.HospitalId.Value != visitHospitalId.Value)
        {
            throw new ForbiddenException("Nhân sự không có quyền tiếp tục lượt khám thuộc cơ sở y tế khác.");
        }

        var hasRequiredPerm = actor.PermissionCodes.Contains(PermissionCodes.PatientCheckin) ||
                              actor.PermissionCodes.Contains(PermissionCodes.QueueManage);
        if (!hasRequiredPerm)
        {
            throw new ForbiddenException("Tài khoản chưa được cấp quyền PATIENT_CHECKIN hoặc QUEUE_MANAGE để tiếp tục lượt khám.");
        }
    }

    private static void EnsureDeferPermission(ActorContext actor, long? visitHospitalId)
    {
        // No SYSTEM_ADMIN bypass (DevSecOps F4).
        var isAllowedStaffRole = actor.RoleCodes.Contains(RoleNames.Reception) ||
                                 actor.RoleCodes.Contains(RoleNames.DiagnosticStaff) ||
                                 actor.RoleCodes.Contains(RoleNames.Doctor) ||
                                 actor.RoleCodes.Contains(RoleNames.HospitalAdmin);
        if (!isAllowedStaffRole)
        {
            throw new ForbiddenException("Người dùng không có quyền ghi nhận hoãn hoặc rời viện cho lượt khám.");
        }

        var staffHospitalId = actor.Doctor?.HospitalId ?? actor.Staff?.HospitalId;
        if (actor.Staff is null ||
            !string.Equals(actor.Staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
            !staffHospitalId.HasValue)
        {
            throw new ForbiddenException("Hồ sơ nhân sự không ở trạng thái hoạt động hợp lệ.");
        }

        if (visitHospitalId.HasValue && staffHospitalId.Value != visitHospitalId.Value)
        {
            throw new ForbiddenException("Nhân sự không có quyền thao tác lượt khám thuộc cơ sở y tế khác.");
        }
    }
}
