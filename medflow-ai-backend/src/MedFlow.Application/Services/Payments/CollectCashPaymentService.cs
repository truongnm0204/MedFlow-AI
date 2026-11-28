using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Persistence;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Realtime;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Application.Services.Payments;

public sealed class CollectCashPaymentService : ICollectCashPaymentService
{
    public const string AuditAction = "CASH_PAYMENT_COLLECTED";
    public const string ForbiddenMessage = "Bạn không có quyền thu tiền mặt tại quầy cho lượt khám này.";
    public const string MissingPaymentCollectPermissionMessage = "[PAYMENT_COLLECT_REQUIRED] Tài khoản chưa được cấp quyền thu tiền mặt (PAYMENT_COLLECT).";
    public const string InvalidIdempotencyKeyMessage = "Tham số idempotencyKey là bắt buộc (1–100 ký tự) khi thu tiền mặt.";
    public const string MissingChargesMessage = "Vui lòng chọn ít nhất một khoản phí cần thu tiền mặt.";
    public const string ChargeAlreadyPaidMessage = "[CHARGE_ALREADY_PAID] Một hoặc nhiều khoản phí đã được thanh toán.";
    public const string IdempotencyMismatchMessage = "[IDEMPOTENCY_PAYLOAD_MISMATCH] Khóa idempotency đã được sử dụng cho phiếu thu tiền mặt khác.";

    private const int MaxIdempotencyKeyLength = 100;
    private const int MaxNotesLength = 500;

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;
    private readonly IPaymentEnrollmentIntentProcessor _intentProcessor;

    public CollectCashPaymentService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IRealtimeNotificationPublisher? realtimePublisher = null,
        IPaymentEnrollmentIntentProcessor? intentProcessor = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _realtimePublisher = realtimePublisher ?? NullRealtimeNotificationPublisher.Instance;
        _intentProcessor = intentProcessor
            ?? new PaymentEnrollmentIntentProcessor(db, _timeProvider, realtimePublisher: _realtimePublisher);
    }

    public async Task<CollectCashPaymentResponseDto> CollectAsync(
        long actorUserId,
        string? idOrCode,
        CollectCashPaymentRequestDto? request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ValidationException(InvalidIdempotencyKeyMessage);

        var idempotencyKey = request.IdempotencyKey.Trim();
        if (idempotencyKey.Length > MaxIdempotencyKeyLength)
            throw new ValidationException(InvalidIdempotencyKeyMessage);

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > MaxNotesLength })
            throw new ValidationException($"Ghi chú phiếu thu không được vượt quá {MaxNotesLength} ký tự.");

        var requestedChargeIds = VisitPaymentAccess.ParseExplicitVisitChargeIds(
            request.VisitChargeId,
            request.VisitChargeIds,
            GenerateQrPaymentService.InvalidVisitChargeIdMessage);

        if (requestedChargeIds is null || requestedChargeIds.Count == 0)
            throw new ValidationException(MissingChargesMessage);

        var staff = await AuthorizeCashCollectorAsync(actorUserId, ct);

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(idOrCode);
        var visit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct)
            ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

        var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
        if (hospitalIds.Count == 0)
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        if (hospitalIds.Count > 1)
            throw new ConflictException(VisitPaymentAccess.HospitalConflictMessage);

        var visitHospitalId = hospitalIds.First();
        if (staff.HospitalId != visitHospitalId)
            throw new ForbiddenException(ForbiddenMessage);

        var visitId = visit.Id;
        CollectCashPaymentResponseDto response;
        long? settledPaymentIdForEnrollment = null;
        var shouldPublishRealtime = false;
        var queueNotifications = new List<QueueChangedNotification>();

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                // ADR-0061 strict lock order: [02 patients] -> 09 visits -> 12 diagnostic_orders -> 15 visit_charges -> 16 payments -> 16b payment_allocations -> 16c payment_enrollment_intents
                // D2: việc tự lấy số CLS sau thanh toán do hệ thống quyết định, nên luôn khóa bệnh nhân
                // (nếu có) để tuần tự hóa presence + intent với các luồng lấy số khác.
                await PaymentAllocationLookup.AcquireVisitPaymentChainLocksAsync(
                    _db,
                    visitId,
                    ct,
                    patientIdToLock: visit.PatientId);

                if (_db is DbContext trackerCtx)
                    trackerCtx.ChangeTracker.Clear();

                var lockedVisit = await _db.Visits.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == visitId, ct)
                    ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

                // Re-verify live actor status, hospital scope, and PAYMENT_COLLECT permission inside transaction
                var verifiedCollector = await VisitPaymentAccess.RevalidateActorPostLockAsync(
                    _db,
                    actorUserId,
                    lockedVisit,
                    _timeProvider,
                    ForbiddenMessage,
                    [PermissionCodes.PaymentCollect],
                    MissingPaymentCollectPermissionMessage,
                    allowPatient: false,
                    allowDoctor: false,
                    ct: ct);
                staff = verifiedCollector.Staff!;
                visitHospitalId = verifiedCollector.VisitHospitalId;

                // Check IdempotencyKey post-lock first
                var existingByKey = await _db.Payments.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, ct);
                if (existingByKey is not null)
                {
                    var (replayDto, hasIntentOnReplay) = await ValidateAndBuildIdempotentCashReplayAsync(
                        lockedVisit,
                        staff,
                        existingByKey,
                        requestedChargeIds,
                        ct);
                    await tx.CommitAsync(ct);

                    if (hasIntentOnReplay)
                    {
                        replayDto = await AttachPostCommitEnrollmentSafeAsync(replayDto, existingByKey.Id, ipAddress, ct);
                    }

                    return replayDto;
                }

                if (string.Equals(lockedVisit.Status?.Trim(), PaymentStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lockedVisit.Status?.Trim(), "NO_SHOW", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConflictException(GenerateQrPaymentService.VisitBlockedMessage);
                }

                var assessment = await VisitChargeAssessor.AssessAsync(_db, _timeProvider, lockedVisit, ct);
                var trackedCharges = await _db.VisitCharges
                    .Where(c => c.VisitId == visitId)
                    .OrderBy(c => c.Id)
                    .ToListAsync(ct);

                var trackedPayments = await _db.Payments
                    .Where(p => p.VisitId == visitId)
                    .OrderBy(p => p.Id)
                    .ToListAsync(ct);

                var trackedAllocations = await PaymentAllocationLookup.TryLoadTrackedForVisitAsync(_db, visitId, ct);
                var trackedIntents = await PaymentAllocationLookup.TryLoadTrackedIntentsForVisitAsync(_db, visitId, ct);
                var (effectiveAllocations, _) = PaymentAllocationLookup.BuildEffectiveAllocations(
                    visitId,
                    trackedPayments,
                    trackedAllocations);

                var targetSet = requestedChargeIds.ToHashSet();
                if (effectiveAllocations.Any(a => targetSet.Contains(a.VisitChargeId) &&
                                                  string.Equals(a.Status, PaymentAllocationStatuses.Paid, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ConflictException(ChargeAlreadyPaidMessage);
                }

                var selectedCharges = new List<VisitCharge>(requestedChargeIds.Count);
                decimal totalAmount = 0m;

                foreach (var chargeId in requestedChargeIds)
                {
                    var target = trackedCharges.FirstOrDefault(c => c.Id == chargeId);
                    if (target is null)
                        throw new NotFoundException("Không tìm thấy khoản phí thuộc lượt khám này.");

                    var chargeStatus = string.IsNullOrWhiteSpace(target.Status)
                        ? VisitChargeStatuses.Active
                        : target.Status.Trim().ToUpperInvariant();

                    if (chargeStatus != VisitChargeStatuses.Active)
                        throw new ConflictException(GenerateQrPaymentService.ChargeCancelledMessage);

                    await EnsureDiagnosticOrderEligibleForCashAsync(target, ct);

                    var line = assessment.Lines.FirstOrDefault(l => l.ChargeId == target.Id);
                    if (line is null ||
                        line.ChargeDataStatus is VisitChargesService.ChargeDataInvalid or VisitChargesService.ChargeDataUnpriced ||
                        line.LineSettlementStatus is VisitChargesService.SettlementMismatch or VisitChargesService.SettlementUndetermined)
                    {
                        throw new ConflictException(GenerateQrPaymentService.UndeterminedMessage);
                    }

                    if (line.LineSettlementStatus == VisitChargesService.SettlementPaid)
                        throw new ConflictException(ChargeAlreadyPaidMessage);

                    if (line.Payable == false ||
                        line.UnitPrice == 0m ||
                        line.LineSettlementStatus == VisitChargeAssessor.LineSettlementWaived)
                    {
                        throw new ConflictException(GenerateQrPaymentService.NothingDueMessage);
                    }

                    totalAmount += GenerateQrPaymentService.RequireValidIntegerVnd(line.UnitPrice);
                    selectedCharges.Add(target);
                }

                GenerateQrPaymentService.RequireValidIntegerVnd(totalAmount);

                // Promote any explicitly paid UNDECIDED optional DiagnosticOrders -> ACCEPTED
                if (await PromoteUndecidedOrdersAsync(selectedCharges, ct))
                {
                    await _db.SaveChangesAsync(ct);
                }

                // D2 / BR-BF02-06: hệ thống tự chọn chỉ định CLS kế tiếp trong các khoản phí vừa thu
                // (thứ tự tạo chỉ định, chưa xếp hàng, đã chấp nhận). Client không còn truyền chỉ định.
                var enrollOrder = await PaymentEnrollmentIntentProcessor.ResolveNextAutoEnrollOrderAsync(
                    _db,
                    visitId,
                    selectedCharges,
                    ct);
                PaymentEnrollmentIntentProcessor.EnsureEnrollOrderInPaidChargesOrThrow(enrollOrder, selectedCharges);

                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                // Cancel ALL overlapping open PENDING QR attempts (and ALL of their PENDING allocations & intents!)
                CancelOverlappingPendingAttempts(trackedPayments, trackedAllocations, trackedIntents, targetSet, nowUtc);

                var nowLocal = HospitalClock.GetNow(_timeProvider);
                var singleChargeId = selectedCharges.Count == 1 ? selectedCharges[0].Id : (long?)null;

                var cashPayment = Payment.CreateCash(
                    visitId,
                    singleChargeId,
                    totalAmount,
                    staff.Id,
                    idempotencyKey,
                    nowLocal);

                _db.Payments.Add(cashPayment);
                await _db.SaveChangesAsync(ct);

                cashPayment.PaymentCode = "CASH" + cashPayment.Id.ToString(CultureInfo.InvariantCulture);

                var createdAllocations = selectedCharges
                    .Select(c => PaymentAllocation.Create(
                        cashPayment.Id,
                        c.Id,
                        visitId,
                        c.UnitPrice!.Value,
                        PaymentAllocationStatuses.Paid,
                        nowLocal))
                    .ToList();
                PaymentAllocationLookup.TryAddAllocations(_db, createdAllocations);

                if (enrollOrder is not null && lockedVisit.PatientId is long enrollPatientId && enrollPatientId > 0)
                {
                    var todayLocal = DateOnly.FromDateTime(nowLocal);
                    var trackedVisitForPresence = await _db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, ct);
                    var sessionNo = Math.Max(1, lockedVisit.PresenceSessionNo);
                    if (trackedVisitForPresence is not null)
                    {
                        MedFlow.Application.Services.Visits.VisitPresenceEvaluator.StampPresentToday(trackedVisitForPresence, todayLocal, nowLocal);
                        sessionNo = Math.Max(1, trackedVisitForPresence.PresenceSessionNo);
                    }

                    var createdIntent = new PaymentEnrollmentIntent
                    {
                        PaymentId = cashPayment.Id,
                        VisitId = visitId,
                        PatientId = enrollPatientId,
                        DiagnosticOrderId = enrollOrder.Id,
                        RequestedByUserId = actorUserId,
                        RequestedByRole = verifiedCollector.RoleLabel,
                        IdempotencyKey = $"pei:pay:{cashPayment.Id}",
                        PresenceVerifiedDate = todayLocal,
                        PresenceSessionNo = sessionNo,
                        Status = PaymentEnrollmentIntentStatuses.PendingPostCommit,
                        RetryCount = 0,
                        // payment_enrollment_intents dùng timestamp without time zone (giờ bệnh viện).
                        CreatedAt = nowLocal,
                        ExpiresAt = nowLocal.AddMinutes(30),
                    };
                    PaymentAllocationLookup.TryAddIntent(_db, createdIntent);
                    settledPaymentIdForEnrollment = cashPayment.Id;
                }

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = visitHospitalId,
                    Action = AuditAction,
                    EntityType = "PAYMENT",
                    EntityId = cashPayment.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = notes,
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        visitId,
                        visitCode = lockedVisit.VisitCode,
                        paymentId = cashPayment.Id,
                        paymentCode = cashPayment.PaymentCode,
                        paymentMethod = PaymentMethods.Cash,
                        paymentScope = PaymentScopes.Allocated,
                        collectedByStaffId = staff.Id,
                        collectedByStaffName = staff.FullName,
                        actorUserId,
                        hospitalId = visitHospitalId,
                        amount = totalAmount,
                        currency = VisitChargesService.CurrencyVnd,
                        visitChargeIds = requestedChargeIds,
                        idempotencyKey,
                        notes,
                        occurredAtLocal = nowLocal,
                    }),
                    IpAddress = IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
                    OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
                });

                await _db.SaveChangesAsync(ct);

                queueNotifications = await BuildQueueUnlockNotificationsAsync(lockedVisit, visitHospitalId, ct);
                shouldPublishRealtime = true;

                response = BuildResponseDto(
                    lockedVisit,
                    cashPayment,
                    staff,
                    selectedCharges,
                    createdAllocations,
                    assessment.Lines,
                    isIdempotentReplay: false);

                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresUniqueViolation.Is(ex) || PostgresConcurrencyException.IsUniqueViolation(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext dbCtx)
                    dbCtx.ChangeTracker.Clear();

                // Concurrent duplicate idempotencyKey vs concurrent PAID charge race
                var racedByKey = await _db.Payments.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, ct);
                if (racedByKey is not null)
                {
                    var currentVisit = await _db.Visits.AsNoTracking()
                        .FirstOrDefaultAsync(v => v.Id == visitId, ct)
                        ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
                    var (racedReplayDto, hasRacedIntent) = await ValidateAndBuildIdempotentCashReplayAsync(
                        currentVisit,
                        staff,
                        racedByKey,
                        requestedChargeIds,
                        ct);
                    if (hasRacedIntent)
                    {
                        racedReplayDto = await AttachPostCommitEnrollmentSafeAsync(racedReplayDto, racedByKey.Id, ipAddress, ct);
                    }

                    return racedReplayDto;
                }

                throw new ConflictException(ChargeAlreadyPaidMessage);
            }
            catch (Exception ex) when (PostgresConcurrencyException.IsDeadlockOrSerializationFailure(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext dbCtx)
                    dbCtx.ChangeTracker.Clear();
                throw new ConflictException("Giao dịch thu tiền mặt đang có xung đột đồng thời, vui lòng thử lại.");
            }
            catch
            {
                await SafeRollbackAsync(tx, ct);
                throw;
            }
        }

        if (settledPaymentIdForEnrollment is long settledPaymentId)
        {
            response = await AttachPostCommitEnrollmentSafeAsync(response, settledPaymentId, ipAddress, ct);
        }

        if (shouldPublishRealtime)
        {
            await _realtimePublisher.TryPublishVisitPaymentChangedSafeAsync(
                new VisitPaymentChangedNotification(visitId),
                ct);

            foreach (var queueNotification in queueNotifications)
            {
                await _realtimePublisher.TryPublishQueueChangedSafeAsync(queueNotification, ct);
            }
        }

        return response;
    }

    private async Task<CollectCashPaymentResponseDto> AttachPostCommitEnrollmentSafeAsync(
        CollectCashPaymentResponseDto baseDto,
        long paymentId,
        string? ipAddress,
        CancellationToken ct)
    {
        try
        {
            var enrollResult = await _intentProcessor.ProcessPostCommitAsync(paymentId, ipAddress, ct);
            if (enrollResult is null)
            {
                return baseDto;
            }

            return baseDto with
            {
                EnrollmentStatus = enrollResult.EnrollmentStatus,
                EnrollmentOutcomeCode = enrollResult.OutcomeCode,
                EnrollmentOutcomeMessage = enrollResult.OutcomeMessage,
                EnrolledQueueEntry = enrollResult.QueueEntry,
            };
        }
        catch
        {
            // QE-13 / E-BR28: Post-commit enrollment failure MUST NEVER fail the committed PAID cash payment!
            return baseDto with
            {
                EnrollmentStatus = PaymentEnrollmentIntentStatuses.PendingPostCommit,
                EnrollmentOutcomeCode = "ENROLLMENT_DEFERRED_FOR_RECOVERY",
                EnrollmentOutcomeMessage = "Thanh toán tiền mặt đã hoàn tất. Yêu cầu lấy số tự động đang chờ xử lý lại.",
            };
        }
    }

    private async Task<Staff> AuthorizeCashCollectorAsync(long actorUserId, CancellationToken ct)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);

        var (roleIds, roleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);

        // Strict RBAC (§4, PAY-10, PAY-11): ONLY RECEPTION or HOSPITAL_ADMIN (never DOCTOR, DIAGNOSTIC_STAFF, PATIENT, SYSTEM_ADMIN)
        var hasEligibleRole = roleCodes.Contains(RoleNames.Reception) || roleCodes.Contains(RoleNames.HospitalAdmin);
        if (!hasEligibleRole)
            throw new ForbiddenException(ForbiddenMessage);

        // Live DB check for PAYMENT_COLLECT — HOSPITAL_ADMIN MUST NOT bypass!
        var permSet = await VisitPaymentAccess.LoadRolePermissionCodesAsync(_db, roleIds, ct);
        if (!permSet.Contains(PermissionCodes.PaymentCollect))
            throw new ForbiddenException(MissingPaymentCollectPermissionMessage);

        var staff = await _db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct)
            ?? throw new ForbiddenException(ForbiddenMessage);

        if (!string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
            staff.HospitalId is null)
        {
            throw new ForbiddenException(ForbiddenMessage);
        }

        var hospital = await _db.Hospitals.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == staff.HospitalId.Value, ct)
            ?? throw new ForbiddenException(ForbiddenMessage);

        if (hospital.IsActive == false)
            throw new ForbiddenException(VisitPaymentAccess.InactiveHospitalMessage);

        return staff;
    }

    private async Task VerifyLivePaymentCollectPermissionAsync(long actorUserId, CancellationToken ct)
    {
        var (roleIds, roleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);
        if (!roleCodes.Contains(RoleNames.Reception) && !roleCodes.Contains(RoleNames.HospitalAdmin))
            throw new ForbiddenException(ForbiddenMessage);

        var permSet = await VisitPaymentAccess.LoadRolePermissionCodesAsync(_db, roleIds, ct);
        if (!permSet.Contains(PermissionCodes.PaymentCollect))
            throw new ForbiddenException(MissingPaymentCollectPermissionMessage);
    }

    private async Task<(CollectCashPaymentResponseDto Dto, bool HasEnrollmentIntent)> ValidateAndBuildIdempotentCashReplayAsync(
        Visit visit,
        Staff currentStaff,
        Payment existingPayment,
        IReadOnlyList<long> requestedChargeIds,
        CancellationToken ct)
    {
        if (existingPayment.VisitId != visit.Id ||
            !string.Equals(existingPayment.PaymentMethod, PaymentMethods.Cash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(IdempotencyMismatchMessage);
        }

        // D2 / BR-BF02-06: chỉ định CLS do hệ thống tự chọn nên không còn là một phần payload
        // idempotency của client — chỉ so khớp phương thức + tập khoản phí.
        var existingIntents = await PaymentAllocationLookup.TryLoadNoTrackingIntentsForVisitAsync(_db, visit.Id, ct);
        var existingIntent = existingIntents.FirstOrDefault(i => i.PaymentId == existingPayment.Id);

        var persistedAllocs = await PaymentAllocationLookup.TryLoadNoTrackingForVisitAsync(_db, visit.Id, ct);
        var existingAllocs = persistedAllocs
            .Where(pa => pa.PaymentId == existingPayment.Id)
            .OrderBy(pa => pa.VisitChargeId)
            .ToList();

        var existingChargeIds = existingAllocs.Count > 0
            ? existingAllocs.Select(pa => pa.VisitChargeId).Distinct().OrderBy(id => id).ToList()
            : (existingPayment.VisitChargeId.HasValue ? [existingPayment.VisitChargeId.Value] : []);

        if (!existingChargeIds.SequenceEqual(requestedChargeIds))
        {
            throw new ConflictException(IdempotencyMismatchMessage);
        }

        var charges = await _db.VisitCharges.AsNoTracking()
            .Where(c => c.VisitId == visit.Id && existingChargeIds.Contains(c.Id))
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        var assessment = await VisitChargeAssessor.AssessAsync(_db, _timeProvider, visit, ct);
        var collectorStaff = existingPayment.CollectedByStaffId == currentStaff.Id
            ? currentStaff
            : await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == existingPayment.CollectedByStaffId, ct) ?? currentStaff;

        var dto = BuildResponseDto(
            visit,
            existingPayment,
            collectorStaff,
            charges,
            existingAllocs,
            assessment.Lines,
            isIdempotentReplay: true);
        return (dto, existingIntent is not null);
    }

    private async Task EnsureDiagnosticOrderEligibleForCashAsync(VisitCharge charge, CancellationToken ct)
    {
        if (!charge.DiagnosticOrderId.HasValue)
            return;

        try
        {
            var order = await _db.DiagnosticOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == charge.DiagnosticOrderId.Value, ct);

            if (order is null ||
                string.Equals(order.Status?.Trim(), "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.Declined, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.SkippedAfterPaid, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.ReviewRequired, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(GenerateQrPaymentService.DiagnosticCancelledMessage);
            }
        }
        catch (NotSupportedException)
        {
        }
    }

    private async Task<bool> PromoteUndecidedOrdersAsync(IReadOnlyList<VisitCharge> selectedCharges, CancellationToken ct)
    {
        var orderIds = selectedCharges
            .Where(c => c.DiagnosticOrderId.HasValue)
            .Select(c => c.DiagnosticOrderId!.Value)
            .Distinct()
            .ToList();

        if (orderIds.Count == 0)
            return false;

        try
        {
            var orders = await _db.DiagnosticOrders
                .Where(o => orderIds.Contains(o.Id))
                .ToListAsync(ct);

            var promoted = false;
            foreach (var order in orders)
            {
                if (string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.Undecided, StringComparison.OrdinalIgnoreCase))
                {
                    order.SelectionStatus = DiagnosticSelectionStatuses.Accepted;
                    promoted = true;
                }
            }

            return promoted;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static void CancelOverlappingPendingAttempts(
        IReadOnlyList<Payment> trackedPayments,
        IReadOnlyList<PaymentAllocation> trackedAllocations,
        IReadOnlyList<PaymentEnrollmentIntent> trackedIntents,
        IReadOnlySet<long> targetChargeSet,
        DateTime nowUtc)
    {
        var pendingAllocsByPayment = trackedAllocations
            .Where(pa => string.Equals(pa.Status, PaymentAllocationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            .GroupBy(pa => pa.PaymentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var p in trackedPayments)
        {
            if (!string.Equals(p.Status?.Trim(), PaymentStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                continue;

            var overlaps = false;
            if (pendingAllocsByPayment.TryGetValue(p.Id, out var pAllocs))
            {
                overlaps = pAllocs.Any(pa => targetChargeSet.Contains(pa.VisitChargeId));
            }
            else if (p.VisitChargeId.HasValue)
            {
                overlaps = targetChargeSet.Contains(p.VisitChargeId.Value);
            }

            if (!overlaps)
                continue;

            p.Status = PaymentStatuses.Cancelled;
            if (pendingAllocsByPayment.TryGetValue(p.Id, out var toCancel))
            {
                foreach (var pa in toCancel)
                    pa.Status = PaymentAllocationStatuses.Cancelled;
            }

            foreach (var pi in trackedIntents.Where(i =>
                         i.PaymentId == p.Id &&
                         string.Equals(i.Status, PaymentEnrollmentIntentStatuses.PendingSettlement, StringComparison.OrdinalIgnoreCase)))
            {
                pi.Status = PaymentEnrollmentIntentStatuses.Cancelled;
                pi.OutcomeCode = "PAYMENT_CANCELLED";
                pi.ProcessedAt = HospitalClock.EnsureHospitalLocal(nowUtc);
            }
        }
    }

    private async Task<List<QueueChangedNotification>> BuildQueueUnlockNotificationsAsync(
        Visit visit,
        long hospitalId,
        CancellationToken ct)
    {
        try
        {
            var entries = await _db.QueueEntries.AsNoTracking()
                .Where(q => q.VisitId == visit.Id &&
                            q.Status != null &&
                            q.Status.ToUpper() != "COMPLETED" &&
                            q.Status.ToUpper() != "CANCELLED")
                .ToListAsync(ct);

            return entries
                .Select(q => new QueueChangedNotification(
                    HospitalId: hospitalId,
                    ServicePointId: q.CurrentServicePointId ?? q.ServicePointId,
                    RoomId: visit.ExaminationRoomId,
                    AppointmentId: visit.AppointmentId))
                .Distinct()
                .ToList();
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private static CollectCashPaymentResponseDto BuildResponseDto(
        Visit visit,
        Payment payment,
        Staff collectorStaff,
        IReadOnlyList<VisitCharge> charges,
        IReadOnlyList<PaymentAllocation> allocations,
        IReadOnlyList<VisitChargeLineDto> assessedLines,
        bool isIdempotentReplay)
    {
        var lineByChargeId = assessedLines
            .Where(l => l.ChargeId.HasValue)
            .ToDictionary(l => l.ChargeId!.Value);

        var dtoAllocations = allocations.Count > 0
            ? allocations
                .OrderBy(pa => pa.VisitChargeId)
                .Select(pa =>
                {
                    lineByChargeId.TryGetValue(pa.VisitChargeId, out var line);
                    var charge = charges.FirstOrDefault(c => c.Id == pa.VisitChargeId);
                    return new PaymentAllocationItemDto(
                        pa.Id > 0 ? pa.Id : null,
                        pa.VisitChargeId,
                        line?.ChargeType ?? charge?.ChargeType ?? VisitChargeTypes.Examination,
                        line?.DiagnosticOrderId ?? charge?.DiagnosticOrderId,
                        line?.ServiceName,
                        pa.AllocatedAmount,
                        pa.Status);
                }).ToList()
            : charges
                .OrderBy(c => c.Id)
                .Select(c =>
                {
                    lineByChargeId.TryGetValue(c.Id, out var line);
                    return new PaymentAllocationItemDto(
                        null,
                        c.Id,
                        line?.ChargeType ?? c.ChargeType,
                        line?.DiagnosticOrderId ?? c.DiagnosticOrderId,
                        line?.ServiceName,
                        c.UnitPrice ?? 0m,
                        PaymentAllocationStatuses.Paid);
                }).ToList();

        return new CollectCashPaymentResponseDto(
            visit.Id,
            visit.VisitCode,
            payment.Id,
            payment.PaymentCode ?? $"CASH{payment.Id}",
            payment.Amount ?? dtoAllocations.Sum(a => a.AllocatedAmount),
            VisitChargesService.CurrencyVnd,
            payment.Status ?? PaymentStatuses.Paid,
            payment.PaymentMethod ?? PaymentMethods.Cash,
            payment.CollectedByStaffId ?? collectorStaff.Id,
            collectorStaff.FullName,
            payment.IdempotencyKey ?? "",
            payment.PaidAt ?? payment.CreatedAt ?? DateTime.UtcNow,
            isIdempotentReplay,
            dtoAllocations);
    }

    private static async Task SafeRollbackAsync(IDbContextTransaction tx, CancellationToken ct)
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
