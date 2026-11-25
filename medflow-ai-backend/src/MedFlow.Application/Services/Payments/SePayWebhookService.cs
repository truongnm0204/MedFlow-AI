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

                // ADR-0059 strict lock order: visits (09) -> diagnostic_orders (12) -> visit_charges (15) -> payments (16)
                if (candidatePayment.VisitId is long preVisitId)
                {
                    await _db.TryLockVisitForUpdateAsync(preVisitId, ct);
                }

                VisitCharge? preCharge = null;
                if (candidatePayment.VisitChargeId is long preChargeId)
                {
                    preCharge = await TryLoadChargeAsNoTrackingAsync(preChargeId, ct);
                    if (preCharge?.DiagnosticOrderId is long preDiagOrderId)
                    {
                        await _db.TryLockDiagnosticOrderForUpdateAsync(preDiagOrderId, ct);
                    }

                    await _db.TryLockVisitChargeForUpdateAsync(preChargeId, ct);
                }

                var siblingPaymentIds = await LoadSiblingPaymentIdsForLockAsync(candidatePayment, ct);
                if (siblingPaymentIds.Count > 0)
                {
                    await _db.TryLockPaymentsForUpdateAsync(siblingPaymentIds, ct);
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

                var charge = payment.VisitChargeId is long lockedChargeId
                    ? await TryLoadTrackedChargeAsync(lockedChargeId, ct)
                    : null;

                var diagOrder = charge?.DiagnosticOrderId is long lockedOrderId
                    ? await TryLoadDiagnosticOrderAsNoTrackingAsync(lockedOrderId, ct)
                    : null;

                var siblingChargePayments = charge is not null
                    ? await _db.Payments
                        .Where(p => p.VisitChargeId == charge.Id)
                        .OrderBy(p => p.Id)
                        .ToListAsync(ct)
                    : [];

                var hospitalId = await ResolveVisitHospitalIdAsync(visit, ct);
                var status = Normalize(payment.Status);
                notifyVisitId = payment.VisitId;

                if (status == PaymentStatuses.Paid)
                {
                    outcome = PaymentGatewayOutcomes.AlreadyPaid;
                }
                else if (IsServiceOrVisitCancelled(visit, diagOrder, charge))
                {
                    // ADR-0059 §2.2.B / §3.4: Webhook after Visit / DiagnosticOrder / VisitCharge cancellation
                    outcome = CancelledServiceRefundRequiredOutcome;

                    if (charge is not null)
                    {
                        charge.Status = VisitChargeStatuses.CancelledRefundPending;
                        charge.CancelledAt ??= HospitalClock.GetNow(_timeProvider);
                    }

                    foreach (var sib in siblingChargePayments.Where(s => s.Id != payment.Id && Normalize(s.Status) == PaymentStatuses.Pending))
                    {
                        sib.Status = PaymentStatuses.Expired;
                    }

                    if (status is PaymentStatuses.Pending or PaymentStatuses.Expired)
                    {
                        payment.Status = MismatchPaymentStatus;
                        payment.ExternalReference = referenceCode;
                    }

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
                            expectedAmount = charge?.UnitPrice ?? payment.Amount,
                            referenceCode,
                            currency = VisitChargesService.CurrencyVnd,
                            outcome,
                            reasonCode = CancelledServiceReasonCode,
                            visitId = payment.VisitId,
                            visitStatus = visit?.Status,
                            visitChargeId = charge?.Id,
                            chargeStatus = charge?.Status,
                            diagnosticOrderId = diagOrder?.Id,
                            diagnosticOrderStatus = diagOrder?.Status,
                        });
                }
                else if (status is PaymentStatuses.Pending or PaymentStatuses.Expired)
                {
                    var alreadyPaidSibling = siblingChargePayments
                        .FirstOrDefault(s => s.Id != payment.Id && Normalize(s.Status) == PaymentStatuses.Paid);

                    var expectedAmount = charge?.UnitPrice ?? payment.Amount;
                    var isChargeScoped = payment.VisitChargeId.HasValue ||
                                         string.Equals(payment.PaymentScope, PaymentScopes.Charge, StringComparison.OrdinalIgnoreCase);

                    if (alreadyPaidSibling is not null)
                    {
                        // Old EXPIRED QR paid after newer QR was already PAID (ux_payments_one_paid_per_charge)
                        outcome = DuplicateChargeReconcileOutcome;
                        payment.Status = MismatchPaymentStatus;
                        payment.ExternalReference = referenceCode;

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
                                expectedAmount,
                                referenceCode,
                                currency = VisitChargesService.CurrencyVnd,
                                outcome,
                                reasonCode = DuplicateChargeReasonCode,
                                visitId = payment.VisitId,
                                visitChargeId = charge?.Id,
                                alreadyPaidPaymentId = alreadyPaidSibling.Id,
                                alreadyPaidPaymentCode = alreadyPaidSibling.PaymentCode,
                            });
                    }
                    else if (expectedAmount is not decimal expected ||
                             expected <= 0m ||
                             payment.Amount is null ||
                             payment.Amount <= 0m ||
                             (charge?.UnitPrice is decimal chargePrice && payment.Amount != chargePrice))
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

                            AddConfirmedAudit(hospitalId, payment, code!, amount, totalReceived, expected, externalId, outcome);
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
                                    visitChargeId = charge?.Id,
                                });
                        }
                    }
                    else if (amount != expected)
                    {
                        // ADR-0059 Exact Settlement Only for CHARGE-scoped payments:
                        // underpayment (< expected) or overpayment (> expected) transitions to MISMATCH without unlocking.
                        outcome = amount < expected
                            ? PaymentGatewayOutcomes.Partial
                            : PaymentGatewayOutcomes.Overpaid;

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
                                expectedAmount = expected,
                                referenceCode,
                                currency = VisitChargesService.CurrencyVnd,
                                outcome,
                                visitId = payment.VisitId,
                                visitChargeId = charge?.Id,
                            });
                    }
                    else
                    {
                        // Exact match (amount == expected) on active Visit / Order / Charge:
                        // If customer paid an old EXPIRED QR while a newer PENDING QR was open on the same charge,
                        // expire the unused newer PENDING QR and transition the paid row -> PAID!
                        foreach (var newerPending in siblingChargePayments.Where(s => s.Id != payment.Id && Normalize(s.Status) == PaymentStatuses.Pending))
                        {
                            newerPending.Status = PaymentStatuses.Expired;
                        }

                        payment.Status = PaymentStatuses.Paid;
                        payment.PaidAt = ResolvePaidAt(request.TransactionDate);
                        payment.ExternalReference = referenceCode;
                        outcome = PaymentGatewayOutcomes.Paid;

                        AddConfirmedAudit(hospitalId, payment, code!, amount, amount, expected, externalId, outcome);
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
                // Duplicate (provider, external_id) webhook replay: rollback & return IGNORED with zero SignalR side effects.
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

    private static bool IsServiceOrVisitCancelled(
        Visit? visit,
        DiagnosticOrder? diagOrder,
        VisitCharge? charge)
    {
        if (visit is not null)
        {
            var visitStatus = Normalize(visit.Status);
            if (visitStatus is "CANCELLED" or "NO_SHOW")
                return true;
        }

        if (diagOrder is not null && Normalize(diagOrder.Status) == "CANCELLED")
            return true;

        if (charge is not null)
        {
            var chargeStatus = Normalize(charge.Status);
            if (chargeStatus is VisitChargeStatuses.Cancelled or VisitChargeStatuses.CancelledRefundPending)
                return true;
        }

        return false;
    }

    private async Task<List<long>> LoadSiblingPaymentIdsForLockAsync(Payment candidate, CancellationToken ct)
    {
        if (candidate.VisitChargeId is long chargeId)
        {
            return await _db.Payments.AsNoTracking()
                .Where(p => p.VisitChargeId == chargeId || p.Id == candidate.Id)
                .OrderBy(p => p.Id)
                .Select(p => p.Id)
                .Distinct()
                .ToListAsync(ct);
        }

        if (candidate.VisitId is long visitId)
        {
            return await _db.Payments.AsNoTracking()
                .Where(p => p.VisitId == visitId)
                .OrderBy(p => p.Id)
                .Select(p => p.Id)
                .Distinct()
                .ToListAsync(ct);
        }

        return [candidate.Id];
    }

    private async Task<VisitCharge?> TryLoadChargeAsNoTrackingAsync(long chargeId, CancellationToken ct)
    {
        try
        {
            return await _db.VisitCharges.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == chargeId, ct);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<VisitCharge?> TryLoadTrackedChargeAsync(long chargeId, CancellationToken ct)
    {
        try
        {
            return await _db.VisitCharges
                .FirstOrDefaultAsync(c => c.Id == chargeId, ct);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<DiagnosticOrder?> TryLoadDiagnosticOrderAsNoTrackingAsync(long orderId, CancellationToken ct)
    {
        try
        {
            return await _db.DiagnosticOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        }
        catch (NotSupportedException)
        {
            return null;
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
        string outcome)
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
            // Transaction already aborted by PostgreSQL.
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
