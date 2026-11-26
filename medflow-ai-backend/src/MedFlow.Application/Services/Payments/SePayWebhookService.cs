using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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

public sealed class SePayWebhookService : ISePayWebhookService
{
    public const string Provider = "SEPAY";
    public const string AuditAction = "PAYMENT_CONFIRMED";
    public const string CancelledServiceAuditAction = "PAYMENT_RECEIVED_FOR_CANCELLED_SERVICE";
    public const string LateDuplicateAuditAction = "LATE_DUPLICATE_TRANSFER_DETECTED";
    public const string ReconciliationRequiredAuditAction = "PAYMENT_RECONCILIATION_REQUIRED";

    /// <summary>Fits payment_gateway_events.outcome VARCHAR(24); full reason recorded in AuditLog.ChangeData.</summary>
    public const string CancelledServiceRefundRequiredOutcome = "CANCELLED_REFUND_PENDING";
    public const string DuplicateChargeReconcileOutcome = "DUPLICATE_RECONCILE";

    public const string CancelledServiceReasonCode = "CANCELLED_SERVICE_REFUND_REQUIRED";
    public const string DuplicateChargeReasonCode = "DUPLICATE_CHARGE_RECONCILE";
    public const string MismatchPaymentStatus = VisitChargesService.SettlementMismatch;

    public const string InvalidTransactionMessage = "Mã giao dịch SePay không hợp lệ.";
    public const string InvalidAmountMessage = "Số tiền giao dịch SePay không hợp lệ.";

    private const int MaxCodeLength = 40;
    private const int MaxReferenceLength = 150;

    private static readonly Regex CodePattern = new(
        @"\bMF(\d{1,18})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IApplicationDbContext _db;
    private readonly IPaymentGatewayEventStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;

    public SePayWebhookService(
        IApplicationDbContext db,
        IPaymentGatewayEventStore store,
        TimeProvider? timeProvider = null,
        IRealtimeNotificationPublisher? realtimePublisher = null)
    {
        _db = db;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _realtimePublisher = realtimePublisher ?? NullRealtimeNotificationPublisher.Instance;
    }

    public async Task<SePayWebhookResult> HandleAsync(
        SePayWebhookRequest request,
        CancellationToken ct = default)
    {
        if (request.Id <= 0)
            throw new ValidationException(InvalidTransactionMessage);

        if (request.TransferAmount is not decimal amount || amount <= 0)
            throw new ValidationException(InvalidAmountMessage);

        if (!string.Equals(request.TransferType?.Trim(), "in", StringComparison.OrdinalIgnoreCase))
            return new SePayWebhookResult(true, PaymentGatewayOutcomes.Ignored);

        var externalId = request.Id.ToString(CultureInfo.InvariantCulture);
        if (await _store.ExistsAsync(Provider, externalId, ct))
            return new SePayWebhookResult(true, PaymentGatewayOutcomes.Ignored);

        var code = ExtractCode(request);
        var referenceCode = TrimTo(request.ReferenceCode, MaxReferenceLength);

        var outcome = PaymentGatewayOutcomes.Unmatched;
        long? notifyVisitId = null;
        var shouldPublishPaymentChanged = false;
        var queueNotifications = new List<QueueChangedNotification>();

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                Payment? candidatePayment = null;
                if (code is not null)
                {
                    candidatePayment = await _db.Payments.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.PaymentCode == code, ct);
                }

                if (candidatePayment is null)
                {
                    var unmatchedEvent = new PaymentGatewayEvent
                    {
                        Provider = Provider,
                        ExternalId = externalId,
                        PaymentId = null,
                        PaymentCode = code,
                        TransferAmount = amount,
                        ReferenceCode = referenceCode,
                        Outcome = PaymentGatewayOutcomes.Unmatched,
                        ReceivedAt = HospitalClock.GetUtcNow(_timeProvider),
                    };
                    _store.Add(unmatchedEvent);
                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return new SePayWebhookResult(true, PaymentGatewayOutcomes.Unmatched);
                }

                // ADR-0060 strict lock order: visits (09) -> diagnostic_orders (12) -> visit_charges (15) -> payments (16) -> payment_allocations (16b)
                if (candidatePayment.VisitId is long preVisitId)
                {
                    await PaymentAllocationLookup.AcquireVisitPaymentChainLocksAsync(_db, preVisitId, ct);
                }
                else
                {
                    await _db.TryLockPaymentForUpdateAsync(candidatePayment.Id, ct);
                }

                if (_db is DbContext trackerCtx)
                {
                    trackerCtx.ChangeTracker.Clear();
                }

                // Re-check idempotency post-lock
                if (await _store.ExistsAsync(Provider, externalId, ct))
                {
                    await SafeRollbackAsync(tx, ct);
                    return new SePayWebhookResult(true, PaymentGatewayOutcomes.Ignored);
                }

                var payment = await _db.Payments
                    .FirstOrDefaultAsync(p => p.Id == candidatePayment.Id, ct);

                if (payment is null)
                {
                    var vanishedEvent = new PaymentGatewayEvent
                    {
                        Provider = Provider,
                        ExternalId = externalId,
                        PaymentId = null,
                        PaymentCode = code,
                        TransferAmount = amount,
                        ReferenceCode = referenceCode,
                        Outcome = PaymentGatewayOutcomes.Unmatched,
                        ReceivedAt = HospitalClock.GetUtcNow(_timeProvider),
                    };
                    _store.Add(vanishedEvent);
                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return new SePayWebhookResult(true, PaymentGatewayOutcomes.Unmatched);
                }

                var visit = payment.VisitId is long lockedVisitId
                    ? await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == lockedVisitId, ct)
                    : null;

                var allVisitPayments = payment.VisitId is long vpId
                    ? await _db.Payments.Where(p => p.VisitId == vpId).OrderBy(p => p.Id).ToListAsync(ct)
                    : [payment];

                var allVisitAllocations = payment.VisitId is long vaId
                    ? await PaymentAllocationLookup.TryLoadTrackedForVisitAsync(_db, vaId, ct)
                    : [];

                var thisPaymentAllocations = allVisitAllocations
                    .Where(pa => pa.PaymentId == payment.Id)
                    .OrderBy(pa => pa.VisitChargeId)
                    .ToList();

                var targetChargeIds = thisPaymentAllocations.Count > 0
                    ? thisPaymentAllocations.Select(pa => pa.VisitChargeId).Distinct().OrderBy(id => id).ToList()
                    : (payment.VisitChargeId.HasValue ? [payment.VisitChargeId.Value] : []);

                var targetCharges = targetChargeIds.Count > 0
                    ? await TryLoadTrackedChargesAsync(targetChargeIds, ct)
                    : [];

                var targetOrderIds = targetCharges
                    .Where(c => c.DiagnosticOrderId.HasValue)
                    .Select(c => c.DiagnosticOrderId!.Value)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();

                var targetOrders = targetOrderIds.Count > 0
                    ? await TryLoadTrackedOrdersAsync(targetOrderIds, ct)
                    : [];

                var (effectiveAllocations, _) = payment.VisitId is long effVisitId
                    ? PaymentAllocationLookup.BuildEffectiveAllocations(effVisitId, allVisitPayments, allVisitAllocations)
                    : ([], new HashSet<long>());

                var hospitalId = await ResolveVisitHospitalIdAsync(visit, ct);
                var status = Normalize(payment.Status);
                var isAllocatedOrHasRows = thisPaymentAllocations.Count > 0 ||
                                           string.Equals(payment.PaymentScope, PaymentScopes.Allocated, StringComparison.OrdinalIgnoreCase);
                var isChargeScoped = isAllocatedOrHasRows ||
                                     payment.VisitChargeId.HasValue ||
                                     string.Equals(payment.PaymentScope, PaymentScopes.Charge, StringComparison.OrdinalIgnoreCase);

                notifyVisitId = payment.VisitId;
                var nowLocal = HospitalClock.GetNow(_timeProvider);

                if (status == PaymentStatuses.Paid)
                {
                    // PE-02 (ADR-0061 Stage E1): Same externalId replay was already short-circuited above via _store.ExistsAsync -> IGNORED.
                    // Reaching here means a brand-new externalId (distinct bank transfer) hit an already-PAID Payment.
                    // Keep Payment.Status == PAID and all PAID PaymentAllocations 100% untouched, while flagging DUPLICATE_RECONCILE + audit!
                    outcome = DuplicateChargeReconcileOutcome;
                    AddReconciliationAudit(
                        hospitalId,
                        payment,
                        LateDuplicateAuditAction,
                        new
                        {
                            paymentCode = code,
                            provider = Provider,
                            externalId,
                            receivedAmount = amount,
                            expectedAmount = payment.Amount,
                            referenceCode,
                            currency = VisitChargesService.CurrencyVnd,
                            outcome,
                            reasonCode = DuplicateChargeReasonCode,
                            visitId = payment.VisitId,
                            visitChargeId = payment.VisitChargeId,
                            visitChargeIds = targetChargeIds,
                            alreadyPaidPaymentId = payment.Id,
                            alreadyPaidPaymentCode = payment.PaymentCode,
                        });
                }
                else if (IsAnyTargetCancelledOrDeclined(visit, targetOrders, targetCharges))
                {
                    // §2.3: Late transfer after Visit / DiagnosticOrder / VisitCharge cancellation or decline
                    outcome = CancelledServiceRefundRequiredOutcome;

                    var targetSet = targetChargeIds.ToHashSet();
                    var alreadyPaidChargeIds = effectiveAllocations
                        .Where(a => a.PaymentId != payment.Id &&
                                    targetSet.Contains(a.VisitChargeId) &&
                                    Normalize(a.Status) == PaymentAllocationStatuses.Paid)
                        .Select(a => a.VisitChargeId)
                        .ToHashSet();

                    foreach (var charge in targetCharges)
                    {
                        var cs = Normalize(charge.Status);
                        // Never downgrade a charge already PAID by another valid attempt or PAID_NOT_PERFORMED!
                        if (!alreadyPaidChargeIds.Contains(charge.Id) &&
                            cs is VisitChargeStatuses.Cancelled or VisitChargeStatuses.CancelledRefundPending)
                        {
                            charge.Status = VisitChargeStatuses.CancelledRefundPending;
                            charge.CancelledAt ??= nowLocal;
                        }
                    }

                    // Expire any other overlapping PENDING attempts on cancelled charges only
                    // (do NOT expire a newer PENDING QR that covers only remaining ACTIVE charges!)
                    var visitCancelledOrNoShow = visit is not null &&
                                                 Normalize(visit.Status) is "CANCELLED" or "NO_SHOW";
                    var cancelledChargeSet = visitCancelledOrNoShow
                        ? targetSet
                        : targetCharges
                            .Where(c =>
                                Normalize(c.Status) != VisitChargeStatuses.Active ||
                                (c.DiagnosticOrderId.HasValue &&
                                 targetOrders.Any(o =>
                                     o.Id == c.DiagnosticOrderId.Value &&
                                     (Normalize(o.Status) == "CANCELLED" ||
                                      Normalize(o.SelectionStatus) is DiagnosticSelectionStatuses.Declined
                                          or DiagnosticSelectionStatuses.SkippedAfterPaid
                                          or DiagnosticSelectionStatuses.ReviewRequired))))
                            .Select(c => c.Id)
                            .ToHashSet();

                    ExpireOverlappingPendingAttempts(allVisitPayments, allVisitAllocations, payment.Id, cancelledChargeSet);

                    payment.Status = MismatchPaymentStatus;
                    payment.ExternalReference = referenceCode;
                    foreach (var pa in thisPaymentAllocations.Where(pa => Normalize(pa.Status) != PaymentAllocationStatuses.Paid))
                    {
                        pa.Status = PaymentAllocationStatuses.Reconcile;
                    }

                    var expectedSum = targetCharges.Count > 0
                        ? targetCharges.Sum(c => c.UnitPrice ?? 0m)
                        : payment.Amount;

                    AddReconciliationAudit(
                        hospitalId,
                        payment,
                        CancelledServiceAuditAction,
                        new
                        {
                            paymentCode = code,
                            provider = Provider,
                            externalId,
                            receivedAmount = amount,
                            expectedAmount = expectedSum,
                            referenceCode,
                            currency = VisitChargesService.CurrencyVnd,
                            outcome,
                            reasonCode = CancelledServiceReasonCode,
                            visitId = payment.VisitId,
                            visitStatus = visit?.Status,
                            visitChargeId = payment.VisitChargeId,
                            visitChargeIds = targetChargeIds,
                            diagnosticOrderIds = targetOrderIds,
                        });
                }
                else if (targetChargeIds.Count > 0 &&
                         FindAlreadyPaidCompetingAllocation(effectiveAllocations, payment.Id, targetChargeIds) is { } competingPaid)
                {
                    // §2.2 / §2.3: Target charge already PAID by Cash or newer QR (whether this QR was PENDING, EXPIRED, or CANCELLED!)
                    outcome = DuplicateChargeReconcileOutcome;
                    payment.Status = MismatchPaymentStatus;
                    payment.ExternalReference = referenceCode;

                    foreach (var pa in thisPaymentAllocations.Where(pa => Normalize(pa.Status) != PaymentAllocationStatuses.Paid))
                    {
                        pa.Status = PaymentAllocationStatuses.Reconcile;
                    }

                    AddReconciliationAudit(
                        hospitalId,
                        payment,
                        LateDuplicateAuditAction,
                        new
                        {
                            paymentCode = code,
                            provider = Provider,
                            externalId,
                            receivedAmount = amount,
                            expectedAmount = payment.Amount,
                            referenceCode,
                            currency = VisitChargesService.CurrencyVnd,
                            outcome,
                            reasonCode = DuplicateChargeReasonCode,
                            visitId = payment.VisitId,
                            visitChargeId = competingPaid.VisitChargeId,
                            visitChargeIds = targetChargeIds,
                            alreadyPaidPaymentId = competingPaid.PaymentId,
                            alreadyPaidPaymentCode = competingPaid.Payment?.PaymentCode,
                        });
                }
                else if (status is PaymentStatuses.Pending or PaymentStatuses.Expired ||
                         (status == PaymentStatuses.Cancelled && isAllocatedOrHasRows))
                {
                    decimal? expectedAmount;
                    if (thisPaymentAllocations.Count > 0)
                    {
                        expectedAmount = thisPaymentAllocations.Sum(pa => pa.AllocatedAmount);
                    }
                    else if (targetCharges.Count == 1 && targetCharges[0].UnitPrice is decimal singlePrice)
                    {
                        expectedAmount = singlePrice;
                    }
                    else
                    {
                        expectedAmount = payment.Amount;
                    }

                    var chargePriceMismatch = targetCharges.Count > 0 &&
                                              (targetCharges.Count != targetChargeIds.Count ||
                                               targetCharges.Any(c => c.UnitPrice is null or <= 0m) ||
                                               (thisPaymentAllocations.Count == 0 &&
                                                targetCharges.Count == 1 &&
                                                payment.Amount != targetCharges[0].UnitPrice) ||
                                               (thisPaymentAllocations.Count > 0 &&
                                                payment.Amount != expectedAmount));

                    if (expectedAmount is not decimal expected ||
                        expected <= 0m ||
                        payment.Amount is null ||
                        payment.Amount <= 0m ||
                        chargePriceMismatch)
                    {
                        outcome = PaymentGatewayOutcomes.Unmatched;
                    }
                    else if (!isChargeScoped && amount != expected)
                    {
                        // Pre-023 LEGACY_VISIT active pending cutover (ADR-0059 §2.2.A.2a)
                        var totalReceived = (await _store.SumReceivedAmountAsync(code!, ct)) + amount;
                        if (totalReceived < expected)
                        {
                            outcome = PaymentGatewayOutcomes.Partial;
                        }
                        else if (totalReceived == expected)
                        {
                            payment.Status = PaymentStatuses.Paid;
                            payment.PaidAt = ResolvePaidAt(request.TransactionDate);
                            payment.ExternalReference = referenceCode;
                            outcome = PaymentGatewayOutcomes.Paid;

                            AddConfirmedAudit(hospitalId, payment, code!, amount, totalReceived, expected, externalId, outcome, targetChargeIds);
                            shouldPublishPaymentChanged = true;

                            if (visit is not null && hospitalId.HasValue)
                            {
                                queueNotifications = await BuildQueueUnlockNotificationsAsync(visit, hospitalId.Value, ct);
                            }
                        }
                        else
                        {
                            outcome = PaymentGatewayOutcomes.Overpaid;
                            payment.Status = MismatchPaymentStatus;
                            payment.ExternalReference = referenceCode;

                            AddReconciliationAudit(
                                hospitalId,
                                payment,
                                ReconciliationRequiredAuditAction,
                                new
                                {
                                    paymentCode = code,
                                    provider = Provider,
                                    externalId,
                                    receivedAmount = amount,
                                    totalReceived,
                                    expectedAmount = expected,
                                    referenceCode,
                                    currency = VisitChargesService.CurrencyVnd,
                                    outcome,
                                    visitId = payment.VisitId,
                                    visitChargeId = payment.VisitChargeId,
                                });
                        }
                    }
                    else if (amount != expected)
                    {
                        // PAY-13: Atomic Partial / Overpaid Mismatch — zero allocations marked PAID!
                        outcome = amount < expected
                            ? PaymentGatewayOutcomes.Partial
                            : PaymentGatewayOutcomes.Overpaid;

                        payment.Status = MismatchPaymentStatus;
                        payment.ExternalReference = referenceCode;

                        foreach (var pa in thisPaymentAllocations)
                        {
                            pa.Status = PaymentAllocationStatuses.Reconcile;
                        }

                        AddReconciliationAudit(
                            hospitalId,
                            payment,
                            ReconciliationRequiredAuditAction,
                            new
                            {
                                paymentCode = code,
                                provider = Provider,
                                externalId,
                                receivedAmount = amount,
                                expectedAmount = expected,
                                referenceCode,
                                currency = VisitChargesService.CurrencyVnd,
                                outcome,
                                visitId = payment.VisitId,
                                visitChargeId = payment.VisitChargeId,
                                visitChargeIds = targetChargeIds,
                            });
                    }
                    else
                    {
                        // PAY-08: Exact match on active unpaid charge(s)!
                        // Cancel/expire any competing PENDING attempts + their PENDING allocations overlapping ANY target charge.
                        var targetSet = targetChargeIds.ToHashSet();
                        ExpireOverlappingPendingAttempts(allVisitPayments, allVisitAllocations, payment.Id, targetSet);

                        payment.Status = PaymentStatuses.Paid;
                        payment.PaidAt = ResolvePaidAt(request.TransactionDate);
                        payment.PaymentMethod ??= PaymentMethods.VietQr;
                        payment.ExternalReference = referenceCode;

                        foreach (var pa in thisPaymentAllocations)
                        {
                            pa.Status = PaymentAllocationStatuses.Paid;
                        }

                        foreach (var ord in targetOrders)
                        {
                            if (Normalize(ord.SelectionStatus) == DiagnosticSelectionStatuses.Undecided)
                            {
                                ord.SelectionStatus = DiagnosticSelectionStatuses.Accepted;
                            }
                        }

                        outcome = PaymentGatewayOutcomes.Paid;

                        AddConfirmedAudit(hospitalId, payment, code!, amount, amount, expected, externalId, outcome, targetChargeIds);
                        shouldPublishPaymentChanged = true;

                        if (visit is not null && hospitalId.HasValue)
                        {
                            queueNotifications = await BuildQueueUnlockNotificationsAsync(visit, hospitalId.Value, ct);
                        }
                    }
                }
                else
                {
                    outcome = PaymentGatewayOutcomes.AlreadyClosed;
                }

                var gatewayEvent = new PaymentGatewayEvent
                {
                    Provider = Provider,
                    ExternalId = externalId,
                    PaymentId = payment.Id,
                    PaymentCode = code,
                    TransferAmount = amount,
                    ReferenceCode = referenceCode,
                    Outcome = outcome,
                    ReceivedAt = HospitalClock.GetUtcNow(_timeProvider),
                };

                _store.Add(gatewayEvent);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresUniqueViolation.Is(ex) || PostgresConcurrencyException.IsUniqueViolation(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext context)
                    context.ChangeTracker.Clear();
                return new SePayWebhookResult(true, PaymentGatewayOutcomes.Ignored);
            }
            catch (Exception ex) when (PostgresConcurrencyException.IsDeadlockOrSerializationFailure(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext context)
                    context.ChangeTracker.Clear();
                throw new ConflictException("Giao dịch đối soát cổng thanh toán đang có xung đột đồng thời, vui lòng thử lại.");
            }
            catch
            {
                await SafeRollbackAsync(tx, ct);
                throw;
            }
        }

        // ADR-0059 Principle #6: SignalR published strictly AFTER tx.CommitAsync(ct) succeeds
        if (shouldPublishPaymentChanged && notifyVisitId is long committedVisitId)
        {
            await _realtimePublisher.TryPublishVisitPaymentChangedSafeAsync(
                new VisitPaymentChangedNotification(committedVisitId),
                ct);

            foreach (var queueNotification in queueNotifications)
            {
                await _realtimePublisher.TryPublishQueueChangedSafeAsync(queueNotification, ct);
            }
        }

        return new SePayWebhookResult(true, outcome);
    }

    internal static string? ExtractCode(SePayWebhookRequest request)
    {
        var direct = request.Code?.Trim();
        if (!string.IsNullOrEmpty(direct)
            && direct.Length <= MaxCodeLength
            && direct.StartsWith("MF", StringComparison.OrdinalIgnoreCase)
            && CodePattern.IsMatch(direct)
            && CodePattern.Match(direct).Value.Equals(direct, StringComparison.OrdinalIgnoreCase))
        {
            return direct.ToUpperInvariant();
        }

        if (string.IsNullOrWhiteSpace(request.Content))
            return null;

        var match = CodePattern.Match(request.Content);
        return match.Success ? "MF" + match.Groups[1].Value : null;
    }

    private static bool IsAnyTargetCancelledOrDeclined(
        Visit? visit,
        IReadOnlyList<DiagnosticOrder> diagOrders,
        IReadOnlyList<VisitCharge> charges)
    {
        if (visit is not null)
        {
            var visitStatus = Normalize(visit.Status);
            if (visitStatus is "CANCELLED" or "NO_SHOW")
                return true;
        }

        foreach (var order in diagOrders)
        {
            if (Normalize(order.Status) == "CANCELLED")
                return true;

            var sel = Normalize(order.SelectionStatus);
            if (sel is DiagnosticSelectionStatuses.Declined
                or DiagnosticSelectionStatuses.SkippedAfterPaid
                or DiagnosticSelectionStatuses.ReviewRequired)
            {
                return true;
            }
        }

        foreach (var charge in charges)
        {
            var cs = Normalize(charge.Status);
            if (cs is not null && cs != VisitChargeStatuses.Active)
                return true;
        }

        return false;
    }

    private static EffectiveChargeAllocation? FindAlreadyPaidCompetingAllocation(
        IReadOnlyList<EffectiveChargeAllocation> effectiveAllocations,
        long currentPaymentId,
        IReadOnlyCollection<long> targetChargeIds)
    {
        var targetSet = targetChargeIds.ToHashSet();
        return effectiveAllocations.FirstOrDefault(a =>
            a.PaymentId != currentPaymentId &&
            targetSet.Contains(a.VisitChargeId) &&
            Normalize(a.Status) == PaymentAllocationStatuses.Paid);
    }

    private static void ExpireOverlappingPendingAttempts(
        IReadOnlyList<Payment> allVisitPayments,
        IReadOnlyList<PaymentAllocation> allVisitAllocations,
        long currentPaymentId,
        IReadOnlySet<long> targetSet)
    {
        if (targetSet.Count == 0)
            return;

        var pendingAllocsByPayment = allVisitAllocations
            .Where(pa => Normalize(pa.Status) == PaymentAllocationStatuses.Pending)
            .GroupBy(pa => pa.PaymentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var sib in allVisitPayments)
        {
            if (sib.Id == currentPaymentId || Normalize(sib.Status) != PaymentStatuses.Pending)
                continue;

            var overlaps = false;
            if (pendingAllocsByPayment.TryGetValue(sib.Id, out var sibAllocs))
            {
                overlaps = sibAllocs.Any(pa => targetSet.Contains(pa.VisitChargeId));
            }
            else if (sib.VisitChargeId.HasValue)
            {
                overlaps = targetSet.Contains(sib.VisitChargeId.Value);
            }

            if (!overlaps)
                continue;

            sib.Status = PaymentStatuses.Expired;
            if (pendingAllocsByPayment.TryGetValue(sib.Id, out var toExpire))
            {
                foreach (var pa in toExpire)
                    pa.Status = PaymentAllocationStatuses.Expired;
            }
        }
    }

    private async Task<List<VisitCharge>> TryLoadTrackedChargesAsync(IReadOnlyCollection<long> chargeIds, CancellationToken ct)
    {
        try
        {
            return await _db.VisitCharges
                .Where(c => chargeIds.Contains(c.Id))
                .OrderBy(c => c.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private async Task<List<DiagnosticOrder>> TryLoadTrackedOrdersAsync(IReadOnlyCollection<long> orderIds, CancellationToken ct)
    {
        try
        {
            return await _db.DiagnosticOrders
                .Where(o => orderIds.Contains(o.Id))
                .OrderBy(o => o.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private async Task<long?> ResolveVisitHospitalIdAsync(Visit? visit, CancellationToken ct)
    {
        if (visit is null)
            return null;

        try
        {
            var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
            return hospitalIds.Count == 1 ? hospitalIds.First() : null;
        }
        catch (NotSupportedException)
        {
            return null;
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

    private void AddConfirmedAudit(
        long? hospitalId,
        Payment payment,
        string code,
        decimal received,
        decimal totalReceived,
        decimal expected,
        string externalId,
        string outcome,
        IReadOnlyList<long> visitChargeIds)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = null,
            HospitalId = hospitalId,
            Action = AuditAction,
            EntityType = "PAYMENT",
            EntityId = payment.Id.ToString(CultureInfo.InvariantCulture),
            ChangeData = JsonSerializer.Serialize(new
            {
                paymentCode = code,
                provider = Provider,
                externalId,
                receivedAmount = received,
                totalReceived,
                expectedAmount = expected,
                currency = VisitChargesService.CurrencyVnd,
                outcome,
                visitId = payment.VisitId,
                visitChargeId = payment.VisitChargeId,
                visitChargeIds,
            }),
            OccurredAt = HospitalClock.GetUtcNow(_timeProvider),
        });
    }

    private void AddReconciliationAudit(
        long? hospitalId,
        Payment payment,
        string action,
        object payload)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = null,
            HospitalId = hospitalId,
            Action = action,
            EntityType = "PAYMENT",
            EntityId = payment.Id.ToString(CultureInfo.InvariantCulture),
            ChangeData = JsonSerializer.Serialize(payload),
            OccurredAt = HospitalClock.GetUtcNow(_timeProvider),
        });
    }

    private DateTime ResolvePaidAt(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw)
            && DateTime.TryParseExact(
                raw.Trim(),
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }

        return HospitalClock.GetNow(_timeProvider);
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

    private static string? Normalize(string? status) =>
        string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();

    private static string? TrimTo(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
