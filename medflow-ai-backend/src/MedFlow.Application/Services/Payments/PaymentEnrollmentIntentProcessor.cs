using System.Globalization;
using System.Text.Json;
using MedFlow.Application.Common.Persistence;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Realtime;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

public sealed class PaymentEnrollmentIntentProcessor : IPaymentEnrollmentIntentProcessor
{
    public const string EnrollTargetNotInChargesMessage =
        "[ENROLL_TARGET_NOT_IN_PAYMENT_CHARGES] Chỉ định cận lâm sàng được chọn để tự động lấy số không nằm trong danh sách khoản phí thanh toán của giao dịch này.";

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IVisitChargeSettlementReader _settlementReader;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;

    public PaymentEnrollmentIntentProcessor(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IVisitChargeSettlementReader? settlementReader = null,
        IRealtimeNotificationPublisher? realtimePublisher = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _settlementReader = settlementReader ?? new VisitChargeSettlementReader(db, _timeProvider);
        _realtimePublisher = realtimePublisher ?? NullRealtimeNotificationPublisher.Instance;
    }

    public async Task<PaymentEnrollmentResultDto?> ProcessPostCommitAsync(
        long paymentId,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (paymentId <= 0)
        {
            return null;
        }

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await ExecuteSingleAttemptAsync(paymentId, attempt, ipAddress, ct);
            }
            catch (Exception ex) when (attempt < maxAttempts &&
                                       (PostgresConcurrencyException.IsDeadlockOrSerializationFailure(ex) ||
                                        ex is DbUpdateConcurrencyException))
            {
                if (_db is DbContext dbCtx)
                {
                    dbCtx.ChangeTracker.Clear();
                }
            }
        }

        return await BuildCurrentIntentResultSafeAsync(paymentId, ct);
    }

    public async Task<int> RecoverPendingIntentsAsync(
        int batchSize = 50,
        CancellationToken ct = default)
    {
        var effectiveBatch = Math.Clamp(batchSize, 1, 200);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        List<PaymentEnrollmentIntent> candidates;
        try
        {
            candidates = await _db.PaymentEnrollmentIntents.AsNoTracking()
                .Where(i =>
                    i.Status == PaymentEnrollmentIntentStatuses.PendingPostCommit ||
                    i.Status == PaymentEnrollmentIntentStatuses.PendingSettlement)
                .OrderBy(i => i.CreatedAt)
                .ThenBy(i => i.Id)
                .Take(effectiveBatch)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return 0;
        }

        var processedCount = 0;
        foreach (var candidate in candidates)
        {
            var payment = await _db.Payments.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == candidate.PaymentId, ct);
            var paymentStatus = payment?.Status?.Trim().ToUpperInvariant();

            if (paymentStatus == PaymentStatuses.Paid)
            {
                await ProcessPostCommitAsync(candidate.PaymentId, ct: ct);
                processedCount++;
                continue;
            }

            if (payment is null ||
                paymentStatus is PaymentStatuses.Cancelled or PaymentStatuses.Expired or SePayWebhookService.MismatchPaymentStatus ||
                nowUtc > candidate.ExpiresAt)
            {
                await CloseUnsettledIntentAsync(
                    candidate.Id,
                    nowUtc > candidate.ExpiresAt && paymentStatus == PaymentStatuses.Pending
                        ? PaymentEnrollmentIntentStatuses.Expired
                        : PaymentEnrollmentIntentStatuses.Cancelled,
                    nowUtc > candidate.ExpiresAt && paymentStatus == PaymentStatuses.Pending
                        ? PaymentEnrollmentIntentOutcomeCodes.IntentExpired
                        : "PAYMENT_NOT_SETTLED",
                    nowUtc,
                    ct);
                processedCount++;
            }
        }

        return processedCount;
    }

    private async Task<PaymentEnrollmentResultDto?> ExecuteSingleAttemptAsync(
        long paymentId,
        int attemptNo,
        string? ipAddress,
        CancellationToken ct)
    {
        PaymentEnrollmentIntent? preIntent;
        try
        {
            preIntent = await _db.PaymentEnrollmentIntents.AsNoTracking()
                .FirstOrDefaultAsync(i => i.PaymentId == paymentId, ct);
        }
        catch (NotSupportedException)
        {
            return null;
        }

        if (preIntent is null)
        {
            return null;
        }

        if (IsTerminalIntentStatus(preIntent.Status))
        {
            return await BuildIntentDtoAsync(preIntent, ct);
        }

        var preOrder = await _db.DiagnosticOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == preIntent.DiagnosticOrderId, ct);
        var preVisit = await _db.Visits.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == preIntent.VisitId, ct);

        long hospitalId = 0;
        if (preVisit is not null)
        {
            var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, preVisit, ct);
            if (hospitalIds.Count == 1)
            {
                hospitalId = hospitalIds.First();
            }
        }

        var preRoomIds = hospitalId > 0
            ? await _db.HospitalRooms.AsNoTracking()
                .Where(r => r.HospitalId == hospitalId)
                .Select(r => r.Id)
                .OrderBy(id => id)
                .ToArrayAsync(ct)
            : [];
        var preSpIds = hospitalId > 0
            ? await _db.QueueServicePoints.AsNoTracking()
                .Where(sp => sp.HospitalId == hospitalId)
                .Select(sp => sp.Id)
                .OrderBy(id => id)
                .ToArrayAsync(ct)
            : [];

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        PaymentEnrollmentResultDto? finalDto;
        long? notifySpId = null;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                // ADR-0061 strict lock order:
                // 02 patients -> 04 services -> 06 hospital_rooms -> 09 visits -> 10 queue_service_points -> 11 queue_entries -> 12 diagnostic_orders -> 16 payments -> 16c payment_enrollment_intents
                if (preIntent.PatientId > 0)
                {
                    await _db.TryLockPatientForUpdateAsync(preIntent.PatientId, ct);
                }

                if (preOrder?.ServiceId is long sid && sid > 0)
                {
                    await _db.TryLockServiceForUpdateAsync(sid, ct);
                }

                if (preRoomIds.Length > 0)
                {
                    await _db.TryLockRoomsForUpdateAsync(preRoomIds, ct);
                }

                await _db.TryLockVisitForUpdateAsync(preIntent.VisitId, ct);

                if (preSpIds.Length > 0)
                {
                    await _db.TryLockServicePointsForUpdateAsync(preSpIds, ct);
                }

                var preQeIds = await _db.QueueEntries.AsNoTracking()
                    .Where(q => q.VisitId == preIntent.VisitId)
                    .Select(q => q.Id)
                    .OrderBy(id => id)
                    .ToArrayAsync(ct);
                if (preQeIds.Length > 0)
                {
                    await _db.TryLockQueueEntriesForUpdateAsync(preQeIds, ct);
                }

                await _db.TryLockDiagnosticOrdersForUpdateAsync([preIntent.DiagnosticOrderId], ct);
                await _db.TryLockPaymentForUpdateAsync(paymentId, ct);
                await _db.TryLockPaymentEnrollmentIntentForUpdateAsync(preIntent.Id, ct);

                if (_db is DbContext trackerCtx)
                {
                    trackerCtx.ChangeTracker.Clear();
                }

                var intent = await _db.PaymentEnrollmentIntents.FirstOrDefaultAsync(i => i.Id == preIntent.Id, ct);
                if (intent is null)
                {
                    await tx.CommitAsync(ct);
                    return null;
                }

                if (IsTerminalIntentStatus(intent.Status))
                {
                    await tx.CommitAsync(ct);
                    return await BuildIntentDtoAsync(intent, ct);
                }

                if (attemptNo > 1)
                {
                    intent.RetryCount = Math.Max(intent.RetryCount, attemptNo - 1);
                }

                var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, ct);
                var paymentStatus = payment?.Status?.Trim().ToUpperInvariant();
                if (payment is null || paymentStatus != PaymentStatuses.Paid)
                {
                    if (payment is null ||
                        paymentStatus is PaymentStatuses.Cancelled or PaymentStatuses.Expired or SePayWebhookService.MismatchPaymentStatus)
                    {
                        intent.Status = PaymentEnrollmentIntentStatuses.Cancelled;
                        intent.OutcomeCode = "PAYMENT_NOT_SETTLED";
                        intent.OutcomeMessage = "Giao dịch thanh toán đã đóng hoặc không thành công, hủy yêu cầu lấy số tự động.";
                        intent.ProcessedAt = nowUtc;
                        await _db.SaveChangesAsync(ct);
                    }

                    await tx.CommitAsync(ct);
                    return await BuildIntentDtoAsync(intent, ct);
                }

                if (nowUtc > intent.ExpiresAt)
                {
                    intent.Status = PaymentEnrollmentIntentStatuses.Expired;
                    intent.OutcomeCode = PaymentEnrollmentIntentOutcomeCodes.IntentExpired;
                    intent.OutcomeMessage = "Yêu cầu tự động lấy số sau thanh toán đã quá thời hạn hiệu lực.";
                    intent.ProcessedAt = nowUtc;
                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return await BuildIntentDtoAsync(intent, ct);
                }

                var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == intent.VisitId, ct);
                var order = await _db.DiagnosticOrders.FirstOrDefaultAsync(o => o.Id == intent.DiagnosticOrderId, ct);
                if (visit is null || order is null || order.VisitId != visit.Id)
                {
                    return await MarkSkippedAndCommitAsync(
                        tx,
                        intent,
                        hospitalId > 0 ? hospitalId : null,
                        PaymentEnrollmentIntentOutcomeCodes.DiagnosticOrderNotEligible,
                        "Phiếu chỉ định hoặc lượt khám không còn hợp lệ.",
                        nowUtc,
                        ipAddress,
                        ct);
                }

                if (hospitalId <= 0)
                {
                    var resolvedHospitals = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
                    if (resolvedHospitals.Count == 1)
                    {
                        hospitalId = resolvedHospitals.First();
                    }
                }

                // E2-R01: never substitute a blank default policy to satisfy compilation —
                // an unresolved hospital scope is a real business error, not a fallback case.
                // LoadAsync returns CrossDayVisitPolicySnapshot.Defaults for invalid ids, but
                // reaching that branch means we could not scope the intent to a hospital,
                // so skip-eligible rather than enroll under an implicit policy.
                CrossDayVisitPolicySnapshot? policy = null;
                if (hospitalId > 0)
                {
                    policy = await CrossDayVisitPolicyReader.LoadAsync(_db, hospitalId, nowUtc, ct);
                }

                var trackedVisitQueues = await _db.QueueEntries
                    .Where(q =>
                        q.VisitId == visit.Id ||
                        (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
                    .OrderByDescending(q => q.Id)
                    .ToListAsync(ct);

                PatientSingleActiveQueueGuard.CloseStalePriorDayQueuesOnVisit(trackedVisitQueues, todayLocal, nowLocal);

                // QE-12 / PE-08: Verify presence snapshot AND current live presence session!
                // SePayWebhookService NEVER stamps presence; if patient was remote or departed before settlement, skip cleanly.
                var hasValidPresenceSnapshot =
                    policy is not null &&
                    intent.PresenceVerifiedDate.HasValue &&
                    intent.PresenceVerifiedDate.Value == todayLocal &&
                    intent.PresenceSessionNo.HasValue &&
                    intent.PresenceSessionNo.Value == Math.Max(1, visit.PresenceSessionNo) &&
                    VisitPresenceEvaluator.IsPresentToday(visit, trackedVisitQueues, todayLocal, nowLocal, policy!.PresenceTtlMinutes);

                if (!hasValidPresenceSnapshot)
                {
                    return await MarkSkippedAndCommitAsync(
                        tx,
                        intent,
                        hospitalId > 0 ? hospitalId : null,
                        PaymentEnrollmentIntentOutcomeCodes.PatientNotPresent,
                        VisitQueueEnrollmentService.PatientNotPresentMessage,
                        nowUtc,
                        ipAddress,
                        ct);
                }

                // PE-06 / QE-23: Never auto-enroll while Doctor's clinical consultation session is still open!
                if (VisitQueueEnrollmentService.HasActiveClinicalSession(trackedVisitQueues, todayLocal))
                {
                    return await MarkSkippedAndCommitAsync(
                        tx,
                        intent,
                        hospitalId > 0 ? hospitalId : null,
                        PaymentEnrollmentIntentOutcomeCodes.ClinicalHandoffRequired,
                        VisitQueueEnrollmentService.ClinicalHandoffRequiredMessage,
                        nowUtc,
                        ipAddress,
                        ct);
                }

                var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
                if (visitStatus is "CANCELLED" or "NO_SHOW" or "COMPLETED" or "DONE")
                {
                    return await MarkSkippedAndCommitAsync(
                        tx,
                        intent,
                        hospitalId > 0 ? hospitalId : null,
                        PaymentEnrollmentIntentOutcomeCodes.DiagnosticOrderNotEligible,
                        $"Lượt khám đang ở trạng thái '{visitStatus}', không thể tự động lấy số.",
                        nowUtc,
                        ipAddress,
                        ct);
                }

                if (hospitalId > 0)
                {
                    var activeServing = visit.PatientId is > 0
                        ? await PatientSingleActiveQueueGuard.FindActiveServingQueueForPatientAsync(
                            _db,
                            visit.PatientId.Value,
                            hospitalId,
                            todayLocal,
                            excludeQueueEntryId: null,
                            ct: ct)
                        : null;
                    if (activeServing is not null)
                    {
                        return await MarkSkippedAndCommitAsync(
                            tx,
                            intent,
                            hospitalId,
                            PaymentEnrollmentIntentOutcomeCodes.ActiveQueueAlreadyExists,
                            PatientSingleActiveQueueGuard.ActiveQueueAlreadyExistsMessage,
                            nowUtc,
                            ipAddress,
                            ct);
                    }
                }

                var orderStatus = AppointmentBookingRules.NormalizeStatusKey(order.Status);
                var selectionStatus = string.IsNullOrWhiteSpace(order.SelectionStatus)
                    ? DiagnosticSelectionStatuses.Accepted
                    : order.SelectionStatus.Trim().ToUpperInvariant();
                if (orderStatus is not ("ORDERED" or "CONFIRMED") || selectionStatus != DiagnosticSelectionStatuses.Accepted)
                {
                    return await MarkSkippedAndCommitAsync(
                        tx,
                        intent,
                        hospitalId > 0 ? hospitalId : null,
                        PaymentEnrollmentIntentOutcomeCodes.DiagnosticOrderNotEligible,
                        "Phiếu chỉ định cận lâm sàng không còn ở trạng thái chờ lấy số hợp lệ.",
                        nowUtc,
                        ipAddress,
                        ct);
                }

                if (CrossDayVisitPolicyReader.IsOrderExpiredForExecution(order, policy!.DiagnosticOrderValidityDays, nowLocal))
                {
                    return await MarkSkippedAndCommitAsync(
                        tx,
                        intent,
                        hospitalId > 0 ? hospitalId : null,
                        PaymentEnrollmentIntentOutcomeCodes.DiagnosticOrderExpired,
                        CrossDayVisitPolicyReader.ExpiredOrderDoctorApprovalRequiredMessage,
                        nowUtc,
                        ipAddress,
                        ct);
                }

                var settlement = await _settlementReader.CheckDiagnosticOrderEligibilityAsync(order.Id, ct);
                if (!settlement.IsUnlocked)
                {
                    return await MarkSkippedAndCommitAsync(
                        tx,
                        intent,
                        hospitalId > 0 ? hospitalId : null,
                        PaymentEnrollmentIntentOutcomeCodes.UnpaidDiagnosticFee,
                        settlement.Message ?? "Chỉ định cận lâm sàng chưa hoàn tất thanh toán phí dịch vụ.",
                        nowUtc,
                        ipAddress,
                        ct);
                }

                var roomsById = await _db.HospitalRooms.AsNoTracking()
                    .Where(r => r.HospitalId == hospitalId)
                    .ToDictionaryAsync(r => r.Id, ct);
                var servicePointsById = await _db.QueueServicePoints.AsNoTracking()
                    .Where(sp => sp.HospitalId == hospitalId)
                    .ToDictionaryAsync(sp => sp.Id, ct);
                var departmentsById = await _db.Departments.AsNoTracking()
                    .Where(d => d.HospitalId == hospitalId)
                    .ToDictionaryAsync(d => d.Id, ct);
                var liveService = order.ServiceId.HasValue
                    ? await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == order.ServiceId.Value, ct)
                    : null;
                var diagPoints = servicePointsById.Values
                    .Where(sp => string.Equals(sp.PointType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(sp => sp.Id)
                    .ToList();

                long targetSpId;
                try
                {
                    targetSpId = DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
                        order,
                        liveService,
                        requestedSpId: null,
                        diagPoints,
                        roomsById,
                        trackedVisitQueues,
                        hospitalId,
                        departmentsById);
                }
                catch (ConflictException ex)
                {
                    return await MarkSkippedAndCommitAsync(
                        tx,
                        intent,
                        hospitalId > 0 ? hospitalId : null,
                        PaymentEnrollmentIntentOutcomeCodes.NoActiveCompatibleDiagnosticPoint,
                        ex.Message,
                        nowUtc,
                        ipAddress,
                        ct);
                }

                var maxQueueNumber = await _db.QueueEntries.AsNoTracking()
                    .Where(q =>
                        q.QueueDate == todayLocal &&
                        (q.ServicePointId == targetSpId || q.CurrentServicePointId == targetSpId))
                    .MaxAsync(q => (int?)q.QueueNumber, ct);
                var nextQueueNumber = (maxQueueNumber ?? 0) + 1;

                var referenceClinical = trackedVisitQueues
                    .Where(q => ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType) &&
                                AppointmentBookingRules.NormalizeStatusKey(q.Status) != "CANCELLED")
                    .OrderByDescending(q => q.Id)
                    .FirstOrDefault();
                var derivedPriority = ClinicalQueueRules.NormalizeStoredPriority(referenceClinical?.ManualPriority);

                var newEntry = new QueueEntry
                {
                    VisitId = visit.Id,
                    AppointmentId = null,
                    DiagnosticOrderId = order.Id,
                    PrescriptionId = null,
                    ServicePointId = targetSpId,
                    CurrentServicePointId = targetSpId,
                    QueueDate = todayLocal,
                    QueueNumber = nextQueueNumber,
                    QueueType = QueueEnrollmentTargets.Diagnostic,
                    Status = "WAITING",
                    PresenceStatus = "PRESENT",
                    ArrivedAt = nowLocal,
                    ReadyAt = nowLocal,
                    ArrivalClass = "WALK_IN",
                    ManualPriority = derivedPriority,
                    IdempotencyKey = intent.IdempotencyKey,
                    StateVersion = 1,
                };

                _db.QueueEntries.Add(newEntry);
                await _db.SaveChangesAsync(ct);

                order.QueueEntryId = newEntry.Id;
                order.DeferredUntilDate = null;

                intent.QueueEntryId = newEntry.Id;
                intent.Status = PaymentEnrollmentIntentStatuses.ConsumedEnrolled;
                intent.OutcomeCode = PaymentEnrollmentIntentOutcomeCodes.Enrolled;
                intent.OutcomeMessage = "Đã tự động cấp số hàng đợi cận lâm sàng sau khi hoàn tất thanh toán.";
                intent.ProcessedAt = nowUtc;

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = intent.RequestedByUserId,
                    HospitalId = hospitalId > 0 ? hospitalId : null,
                    Action = VisitQueueEnrollmentService.QueueEnrolledAuditAction,
                    EntityType = "QUEUE_ENTRY",
                    EntityId = newEntry.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = $"Tự động cấp số cận lâm sàng từ PaymentEnrollmentIntent #{intent.Id} (Payment #{paymentId})",
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        intentId = intent.Id,
                        paymentId,
                        visitId = visit.Id,
                        diagnosticOrderId = order.Id,
                        queueEntryId = newEntry.Id,
                        servicePointId = targetSpId,
                        queueNumber = nextQueueNumber,
                        requestedByRole = intent.RequestedByRole,
                    }, AuditJsonOptions),
                    IpAddress = MedFlow.Application.Services.Visits.QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                    OccurredAt = nowUtc,
                });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                notifySpId = targetSpId;
                var ticketDto = VisitQueueEnrollmentService.MapTicketDto(newEntry, servicePointsById, roomsById, isIdempotentReplay: false);
                finalDto = new PaymentEnrollmentResultDto(
                    IntentId: intent.Id,
                    DiagnosticOrderId: intent.DiagnosticOrderId,
                    EnrollmentStatus: intent.Status,
                    OutcomeCode: intent.OutcomeCode,
                    OutcomeMessage: intent.OutcomeMessage,
                    QueueEntry: ticketDto);
            }
            catch (DbUpdateException ex) when (PostgresConcurrencyException.IsUniqueViolation(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext dbCtx)
                {
                    dbCtx.ChangeTracker.Clear();
                }

                // Unique index ux_queue_entries_one_active_post_exam_per_visit or idempotency_key hit concurrently -> mark SKIPPED or return existing!
                return await HandleUniqueRaceAfterRollbackAsync(preIntent.Id, paymentId, hospitalId, nowUtc, ipAddress, ct);
            }
            catch
            {
                await SafeRollbackAsync(tx, ct);
                throw;
            }
        }

        if (notifySpId.HasValue && hospitalId > 0)
        {
            await _realtimePublisher.TryPublishQueueChangedSafeAsync(
                new QueueChangedNotification(HospitalId: hospitalId, ServicePointId: notifySpId.Value),
                ct);
        }

        return finalDto;
    }

    private async Task<PaymentEnrollmentResultDto> MarkSkippedAndCommitAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx,
        PaymentEnrollmentIntent intent,
        long? hospitalId,
        string outcomeCode,
        string outcomeMessage,
        DateTime nowUtc,
        string? ipAddress,
        CancellationToken ct)
    {
        intent.Status = PaymentEnrollmentIntentStatuses.SkippedNotEligible;
        intent.OutcomeCode = outcomeCode;
        intent.OutcomeMessage = outcomeMessage.Length > 500 ? outcomeMessage[..500] : outcomeMessage;
        intent.ProcessedAt = nowUtc;

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = intent.RequestedByUserId,
            HospitalId = hospitalId,
            Action = "PAYMENT_ENROLLMENT_INTENT_SKIPPED",
            EntityType = "PAYMENT_ENROLLMENT_INTENT",
            EntityId = intent.Id.ToString(CultureInfo.InvariantCulture),
            Reason = intent.OutcomeMessage,
            ChangeData = JsonSerializer.Serialize(new
            {
                intentId = intent.Id,
                paymentId = intent.PaymentId,
                visitId = intent.VisitId,
                diagnosticOrderId = intent.DiagnosticOrderId,
                outcomeCode,
                outcomeMessage = intent.OutcomeMessage,
            }, AuditJsonOptions),
            IpAddress = MedFlow.Application.Services.Visits.QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new PaymentEnrollmentResultDto(
            IntentId: intent.Id,
            DiagnosticOrderId: intent.DiagnosticOrderId,
            EnrollmentStatus: intent.Status,
            OutcomeCode: intent.OutcomeCode,
            OutcomeMessage: intent.OutcomeMessage,
            QueueEntry: null);
    }

    private async Task<PaymentEnrollmentResultDto?> HandleUniqueRaceAfterRollbackAsync(
        long intentId,
        long paymentId,
        long hospitalId,
        DateTime nowUtc,
        string? ipAddress,
        CancellationToken ct)
    {
        await using var tx = await _db.BeginTransactionAsync(ct);
        await _db.TryLockPaymentForUpdateAsync(paymentId, ct);
        await _db.TryLockPaymentEnrollmentIntentForUpdateAsync(intentId, ct);

        var intent = await _db.PaymentEnrollmentIntents.FirstOrDefaultAsync(i => i.Id == intentId, ct);
        if (intent is null)
        {
            await tx.CommitAsync(ct);
            return null;
        }

        if (IsTerminalIntentStatus(intent.Status))
        {
            await tx.CommitAsync(ct);
            return await BuildIntentDtoAsync(intent, ct);
        }

        return await MarkSkippedAndCommitAsync(
            tx,
            intent,
            hospitalId > 0 ? hospitalId : null,
            PaymentEnrollmentIntentOutcomeCodes.ActiveQueueAlreadyExists,
            PatientSingleActiveQueueGuard.ActiveQueueAlreadyExistsMessage,
            nowUtc,
            ipAddress,
            ct);
    }

    private async Task CloseUnsettledIntentAsync(
        long intentId,
        string targetStatus,
        string outcomeCode,
        DateTime nowUtc,
        CancellationToken ct)
    {
        await using var tx = await _db.BeginTransactionAsync(ct);
        await _db.TryLockPaymentEnrollmentIntentForUpdateAsync(intentId, ct);
        var intent = await _db.PaymentEnrollmentIntents.FirstOrDefaultAsync(i => i.Id == intentId, ct);
        if (intent is not null && !IsTerminalIntentStatus(intent.Status))
        {
            intent.Status = targetStatus;
            intent.OutcomeCode = outcomeCode;
            intent.ProcessedAt = nowUtc;
            await _db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private async Task<PaymentEnrollmentResultDto?> BuildCurrentIntentResultSafeAsync(long paymentId, CancellationToken ct)
    {
        try
        {
            var intent = await _db.PaymentEnrollmentIntents.AsNoTracking()
                .FirstOrDefaultAsync(i => i.PaymentId == paymentId, ct);
            return intent is not null ? await BuildIntentDtoAsync(intent, ct) : null;
        }
        catch
        {
            return null;
        }
    }

    internal static async Task<PaymentEnrollmentResultDto> BuildIntentDtoAsync(
        IApplicationDbContext db,
        PaymentEnrollmentIntent intent,
        CancellationToken ct)
    {
        VisitQueueEnrollmentTicketDto? ticket = null;
        if (intent.QueueEntryId.HasValue)
        {
            var entry = await db.QueueEntries.AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == intent.QueueEntryId.Value, ct);
            if (entry is not null)
            {
                var spId = entry.CurrentServicePointId ?? entry.ServicePointId;
                var sps = spId.HasValue
                    ? await db.QueueServicePoints.AsNoTracking().Where(sp => sp.Id == spId.Value).ToDictionaryAsync(sp => sp.Id, ct)
                    : new Dictionary<long, QueueServicePoint>();
                var roomIds = sps.Values.Where(sp => sp.RoomId.HasValue).Select(sp => sp.RoomId!.Value).ToList();
                var rooms = roomIds.Count > 0
                    ? await db.HospitalRooms.AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct)
                    : new Dictionary<long, HospitalRoom>();
                ticket = VisitQueueEnrollmentService.MapTicketDto(entry, sps, rooms, isIdempotentReplay: false);
            }
        }

        return new PaymentEnrollmentResultDto(
            IntentId: intent.Id > 0 ? intent.Id : null,
            DiagnosticOrderId: intent.DiagnosticOrderId,
            EnrollmentStatus: intent.Status,
            OutcomeCode: intent.OutcomeCode,
            OutcomeMessage: intent.OutcomeMessage,
            QueueEntry: ticket);
    }

    private Task<PaymentEnrollmentResultDto> BuildIntentDtoAsync(PaymentEnrollmentIntent intent, CancellationToken ct)
        => BuildIntentDtoAsync(_db, intent, ct);

    internal static bool IsTerminalIntentStatus(string? status)
    {
        var normalized = status?.Trim().ToUpperInvariant();
        return normalized is PaymentEnrollmentIntentStatuses.ConsumedEnrolled
            or PaymentEnrollmentIntentStatuses.SkippedNotEligible
            or PaymentEnrollmentIntentStatuses.Cancelled
            or PaymentEnrollmentIntentStatuses.Expired;
    }

    internal static async Task<DiagnosticOrder?> ResolveRequestedEnrollOrderAsync(
        IApplicationDbContext db,
        long visitId,
        long? enrollOrderId,
        string? enrollOrderCode,
        CancellationToken ct)
    {
        if (enrollOrderId is <= 0)
        {
            throw new ValidationException("EnrollDiagnosticOrderId phải lớn hơn 0.");
        }

        var normalizedCode = string.IsNullOrWhiteSpace(enrollOrderCode)
            ? null
            : enrollOrderCode.Trim().ToUpperInvariant();

        if (!enrollOrderId.HasValue && normalizedCode is null)
        {
            return null;
        }

        DiagnosticOrder? order = null;
        if (enrollOrderId.HasValue)
        {
            order = await db.DiagnosticOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == enrollOrderId.Value && o.VisitId == visitId, ct);
            if (order is null)
            {
                throw new NotFoundException("DiagnosticOrder", enrollOrderId.Value);
            }

            if (normalizedCode is not null &&
                !string.IsNullOrWhiteSpace(order.OrderCode) &&
                !string.Equals(order.OrderCode.Trim(), normalizedCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("EnrollDiagnosticOrderId và EnrollDiagnosticOrderCode không thống nhất.");
            }

            return order;
        }

        order = await db.DiagnosticOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.VisitId == visitId && o.OrderCode != null && o.OrderCode.ToUpper() == normalizedCode, ct)
            ?? throw new NotFoundException("DiagnosticOrder", enrollOrderCode!);

        return order;
    }

    internal static void EnsureEnrollOrderInPaidChargesOrThrow(
        DiagnosticOrder? enrollOrder,
        IReadOnlyCollection<VisitCharge> paidCharges)
    {
        if (enrollOrder is null)
        {
            return;
        }

        var orderStatus = AppointmentBookingRules.NormalizeStatusKey(enrollOrder.Status);
        if (orderStatus is "CANCELLED" or "COMPLETED" or "DONE" or "FINALIZED" or "VERIFIED")
        {
            throw new ConflictException(
                $"[DIAGNOSTIC_ORDER_NOT_ELIGIBLE] Phiếu chỉ định #{enrollOrder.Id} đang ở trạng thái '{orderStatus}', không thể gắn yêu cầu tự động lấy số.");
        }

        var includedInPayment = paidCharges.Any(c => c.DiagnosticOrderId == enrollOrder.Id);
        if (!includedInPayment)
        {
            throw new ConflictException(EnrollTargetNotInChargesMessage);
        }
    }

    internal static async Task EnsureStaffHasQueueEnrollWhenIntentRequestedAsync(
        IApplicationDbContext db,
        long actorUserId,
        string roleLabel,
        CancellationToken ct)
    {
        if (!string.Equals(roleLabel, RoleNames.Reception, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(roleLabel, RoleNames.HospitalAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var (roleIds, _) = await VisitPaymentAccess.LoadActorRolesAsync(db, actorUserId, ct);
        var perms = await VisitPaymentAccess.LoadRolePermissionCodesAsync(db, roleIds, ct);
        if (!perms.Contains(PermissionCodes.QueueEnroll))
        {
            throw new ForbiddenException(VisitQueueEnrollmentService.QueueEnrollRequiredMessage);
        }
    }

    private static async Task SafeRollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, CancellationToken ct)
    {
        try
        {
            await tx.RollbackAsync(ct);
        }
        catch
        {
        }
    }
}
