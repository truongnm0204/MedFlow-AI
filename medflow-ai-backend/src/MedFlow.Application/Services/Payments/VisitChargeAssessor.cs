using System.Globalization;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

internal sealed record VisitChargeAssessment(
    IReadOnlyList<VisitChargeLineDto> Lines,
    decimal PayableTotal,
    bool HasUnpricedPayableCharges,
    bool HasInvalidCharges,
    decimal? AmountDue,
    decimal RecognizedPaidTotal,
    string SettlementStatus,
    long? PaymentId,
    string? PaymentCode,
    string? PaymentStatus,
    decimal? PaymentAmount,
    string? PaymentMethod,
    DateTime? PaidAt,
    string? ExternalReference,
    bool CanPayByQr,
    decimal? SelectedAmountDue = null,
    long? ActivePendingQrPaymentId = null,
    string? ActivePendingQrPaymentCode = null,
    decimal? ActivePendingQrAmount = null,
    IReadOnlyList<long>? ActivePendingQrChargeIds = null,
    bool HasReconciliationItems = false);

internal static class VisitChargeAssessor
{
    public const string LineSettlementWaived = "WAIVED";
    public const string LineSettlementCancelled = "CANCELLED";
    public const string LineSettlementPaidNotPerformed = "PAID_NOT_PERFORMED";

    public static async Task<VisitChargeAssessment> AssessAsync(
        IApplicationDbContext db,
        TimeProvider time,
        Visit visit,
        CancellationToken ct)
    {
        var charges = await db.VisitCharges.AsNoTracking()
            .Where(c => c.VisitId == visit.Id)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.VisitId == visit.Id)
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);

        var audits = await VisitWalkInAudits.LoadAsync(db, visit.Id, ct);
        var audit = audits.FirstOrDefault();
        var scheduled = await LoadScheduledPriceAsync(db, time, visit, ct);

        var walkInPaymentMethod = VisitWalkInAudits.NormalizePaymentMethod(audit?.PaymentMethod);
        var visitBlocked = IsStatus(visit.Status, "CANCELLED") || IsStatus(visit.Status, "NO_SHOW");
        var staleCutoff = HospitalClock.GetNow(time).AddMinutes(-GenerateQrPaymentService.PendingExpiryMinutes);

        if (charges.Count == 0)
        {
            return await AssessDerivedLegacyVisitAsync(
                db,
                visitBlocked,
                walkInPaymentMethod,
                staleCutoff,
                audit,
                scheduled,
                payments,
                ct);
        }

        var persistedAllocations = await PaymentAllocationLookup.TryLoadNoTrackingForVisitAsync(db, visit.Id, ct);
        var (effectiveAllocations, allocatedPaymentIds) = PaymentAllocationLookup.BuildEffectiveAllocations(
            visit.Id,
            payments,
            persistedAllocations);

        var diagOrdersById = await TryLoadDiagnosticOrdersAsync(db, charges, ct);

        var paidAll = DedupePaid(payments);
        var recognizedPaid = paidAll.Aggregate(0m, (sum, p) => sum + (p.Amount ?? 0m));
        var paidAmountMissing = paidAll.Any(p => p.Amount is null);

        var legacyPayments = payments
            .Where(p => !p.VisitChargeId.HasValue && !allocatedPaymentIds.Contains(p.Id))
            .ToList();
        var legacyPaid = DedupePaid(legacyPayments);
        var hasLegacyMismatch = legacyPayments.Any(p => IsStatus(p.Status, VisitChargesService.SettlementMismatch));
        var hasActiveLegacyPending = legacyPayments.Any(p => IsActivePending(p, staleCutoff));

        var rawLines = charges
            .Select(c => MapPersistedBase(c, diagOrdersById))
            .ToList();
        rawLines = await FillServiceNamesAsync(db, rawLines, ct);

        var activePayableTotal = rawLines
            .Where(l => IsStatus(l.ChargeStatus, VisitChargeStatuses.Active) && l.Payable == true && l.UnitPrice is >= 0)
            .Sum(l => l.UnitPrice!.Value);

        var hasUnpriced = rawLines.Any(l =>
            IsStatus(l.ChargeStatus, VisitChargeStatuses.Active) &&
            (l.Payable is null || (l.Payable == true && l.UnitPrice is null)));

        var hasInvalid = rawLines.Any(l =>
            IsStatus(l.ChargeStatus, VisitChargeStatuses.Active) &&
            l.ChargeDataStatus == VisitChargesService.ChargeDataInvalid);

        var hasAnyAllocations = effectiveAllocations.Count > 0;

        var isLegacyFullVisitPaid =
            !hasAnyAllocations &&
            !hasUnpriced &&
            !hasInvalid &&
            !paidAmountMissing &&
            !hasLegacyMismatch &&
            legacyPaid.Count == 1 &&
            legacyPaid[0].Amount == activePayableTotal;

        var activeExamLine = rawLines.FirstOrDefault(l =>
            IsStatus(l.ChargeStatus, VisitChargeStatuses.Active) &&
            IsStatus(l.ChargeType, VisitChargeTypes.Examination) &&
            l.Payable == true &&
            l.UnitPrice is > 0m);

        var hasActiveDiagLines = rawLines.Any(l =>
            IsStatus(l.ChargeStatus, VisitChargeStatuses.Active) &&
            IsStatus(l.ChargeType, VisitChargeTypes.Diagnostic));

        var isLegacyExamOnlyPaid =
            !isLegacyFullVisitPaid &&
            !hasLegacyMismatch &&
            legacyPaid.Count == 1 &&
            activeExamLine is not null &&
            hasActiveDiagLines &&
            !effectiveAllocations.Any(a => a.VisitChargeId == activeExamLine.ChargeId) &&
            legacyPaid[0].Amount == activeExamLine.UnitPrice;

        var isLegacyMismatchWithoutChargePayments =
            !hasAnyAllocations &&
            !isLegacyFullVisitPaid &&
            !isLegacyExamOnlyPaid &&
            (hasLegacyMismatch || legacyPaid.Count > 0);

        var evaluatedLines = new List<VisitChargeLineDto>(rawLines.Count);
        var hasReconciliationItems = isLegacyMismatchWithoutChargePayments ||
                                     payments.Any(p => IsStatus(p.Status, VisitChargesService.SettlementMismatch)) ||
                                     effectiveAllocations.Any(a => IsStatus(a.Status, PaymentAllocationStatuses.Reconcile));
        var anyActiveLineMismatch = isLegacyMismatchWithoutChargePayments;
        var anyCancelledRefundPending = false;
        var anyLineUndetermined = paidAmountMissing;

        foreach (var line in rawLines)
        {
            var lineAllocations = line.ChargeId.HasValue
                ? effectiveAllocations.Where(a => a.VisitChargeId == line.ChargeId.Value).ToList()
                : [];

            var diagOrder = line.DiagnosticOrderId.HasValue && diagOrdersById is not null &&
                            diagOrdersById.TryGetValue(line.DiagnosticOrderId.Value, out var ord)
                ? ord
                : null;

            var canExecuteTransition = diagOrder is not null && !IsOrderStartedOrCompleted(diagOrder.Status);
            var isOptionalOrder = diagOrder is not null && diagOrder.RequiredForConclusion == false;

            var paidLineAllocations = DedupePaidAllocations(lineAllocations);
            var primaryPaidAllocation = paidLineAllocations.FirstOrDefault();

            var latestLinePayment = primaryPaidAllocation?.Payment
                ?? lineAllocations.FirstOrDefault()?.Payment
                ?? (!hasAnyAllocations &&
                    (rawLines.Count == 1 || IsStatus(line.ChargeType, VisitChargeTypes.Examination))
                        ? legacyPayments.FirstOrDefault()
                        : null);

            var latestLinePaymentStatus = EffectivePaymentStatus(latestLinePayment, staleCutoff);

            if (IsStatus(line.ChargeStatus, VisitChargeStatuses.PaidNotPerformed))
            {
                evaluatedLines.Add(line with
                {
                    Payable = false,
                    LineSettlementStatus = LineSettlementPaidNotPerformed,
                    PaymentId = latestLinePayment?.Id,
                    PaymentCode = latestLinePayment?.PaymentCode,
                    PaymentStatus = latestLinePaymentStatus ?? PaymentStatuses.Paid,
                    CanPayByQr = false,
                    CanSelect = false,
                    CanDecline = false,
                    CanSkipPaid = false,
                });
                continue;
            }

            if (IsStatus(line.ChargeStatus, VisitChargeStatuses.CancelledRefundPending))
            {
                anyCancelledRefundPending = true;
                hasReconciliationItems = true;
                evaluatedLines.Add(line with
                {
                    Payable = false,
                    LineSettlementStatus = VisitChargesService.SettlementMismatch,
                    PaymentId = latestLinePayment?.Id,
                    PaymentCode = latestLinePayment?.PaymentCode,
                    PaymentStatus = latestLinePaymentStatus,
                    CanPayByQr = false,
                    CanSelect = false,
                    CanDecline = false,
                    CanSkipPaid = false,
                });
                continue;
            }

            if (IsStatus(line.ChargeStatus, VisitChargeStatuses.Cancelled))
            {
                evaluatedLines.Add(line with
                {
                    Payable = false,
                    LineSettlementStatus = LineSettlementCancelled,
                    PaymentId = latestLinePayment?.Id,
                    PaymentCode = latestLinePayment?.PaymentCode,
                    PaymentStatus = latestLinePaymentStatus,
                    CanPayByQr = false,
                    CanSelect = false,
                    CanDecline = false,
                    CanSkipPaid = false,
                });
                continue;
            }

            if (line.ChargeDataStatus is VisitChargesService.ChargeDataInvalid or VisitChargesService.ChargeDataUnpriced)
            {
                anyLineUndetermined = true;
                evaluatedLines.Add(line with
                {
                    LineSettlementStatus = VisitChargesService.SettlementUndetermined,
                    PaymentId = latestLinePayment?.Id,
                    PaymentCode = latestLinePayment?.PaymentCode,
                    PaymentStatus = latestLinePaymentStatus ?? VisitChargesService.SettlementUnpaid,
                    CanPayByQr = false,
                    CanSelect = false,
                    CanDecline = !visitBlocked && isOptionalOrder && canExecuteTransition,
                    CanSkipPaid = false,
                });
                continue;
            }

            if (line.Payable == false || line.UnitPrice == 0m)
            {
                evaluatedLines.Add(line with
                {
                    LineSettlementStatus = LineSettlementWaived,
                    PaymentId = latestLinePayment?.Id,
                    PaymentCode = latestLinePayment?.PaymentCode,
                    PaymentStatus = latestLinePaymentStatus,
                    CanPayByQr = false,
                    CanSelect = !visitBlocked && isOptionalOrder && canExecuteTransition,
                    CanDecline = !visitBlocked && isOptionalOrder && canExecuteTransition,
                    CanSkipPaid = false,
                });
                continue;
            }

            // Active payable line with UnitPrice > 0
            string lineSettlement;
            var lineHasActivePending = lineAllocations.Any(a =>
                                           IsStatus(a.Status, PaymentAllocationStatuses.Pending) &&
                                           a.Payment is not null &&
                                           IsActivePending(a.Payment, staleCutoff)) ||
                                       (!hasAnyAllocations && hasActiveLegacyPending);

            if (isLegacyFullVisitPaid || (isLegacyExamOnlyPaid && line.ChargeId == activeExamLine!.ChargeId))
            {
                var settledLegacy = legacyPaid[0];
                lineSettlement = VisitChargesService.SettlementPaid;
                latestLinePayment = settledLegacy;
                latestLinePaymentStatus = PaymentStatuses.Paid;
            }
            else if (isLegacyMismatchWithoutChargePayments)
            {
                lineSettlement = VisitChargesService.SettlementMismatch;
            }
            else
            {
                var lineMismatchRow = lineAllocations.Any(a =>
                    IsStatus(a.Status, PaymentAllocationStatuses.Reconcile) ||
                    (a.Payment is not null && IsStatus(a.Payment.Status, VisitChargesService.SettlementMismatch)));

                if (paidLineAllocations.Any(a => a.AllocatedAmount is null))
                {
                    lineSettlement = VisitChargesService.SettlementUndetermined;
                    anyLineUndetermined = true;
                }
                else if (paidLineAllocations.Count == 1 && paidLineAllocations[0].AllocatedAmount == line.UnitPrice)
                {
                    // Valid PAID allocation wins even if a separate late/duplicate transfer was flagged RECONCILE!
                    lineSettlement = VisitChargesService.SettlementPaid;
                    latestLinePayment = paidLineAllocations[0].Payment;
                    latestLinePaymentStatus = PaymentStatuses.Paid;
                }
                else if (paidLineAllocations.Count > 0)
                {
                    lineSettlement = VisitChargesService.SettlementMismatch;
                    anyActiveLineMismatch = true;
                    hasReconciliationItems = true;
                }
                else if (lineMismatchRow)
                {
                    lineSettlement = VisitChargesService.SettlementMismatch;
                    anyActiveLineMismatch = true;
                    hasReconciliationItems = true;
                }
                else
                {
                    lineSettlement = VisitChargesService.SettlementUnpaid;
                }
            }

            var diagBlocked = line.DiagnosticOrderId.HasValue &&
                              diagOrdersById is not null &&
                              (!diagOrdersById.TryGetValue(line.DiagnosticOrderId.Value, out var order) ||
                               IsStatus(order.Status, "CANCELLED") ||
                               IsStatus(order.SelectionStatus, DiagnosticSelectionStatuses.Declined) ||
                               IsStatus(order.SelectionStatus, DiagnosticSelectionStatuses.SkippedAfterPaid) ||
                               IsStatus(order.SelectionStatus, DiagnosticSelectionStatuses.ReviewRequired));

            var cashierBlocked = IsStatus(line.ChargeType, VisitChargeTypes.Examination) &&
                                 string.Equals(walkInPaymentMethod, "CASHIER", StringComparison.OrdinalIgnoreCase);

            var lineCanPayByQr = !visitBlocked &&
                                 !diagBlocked &&
                                 !cashierBlocked &&
                                 !hasUnpriced &&
                                 !hasInvalid &&
                                 line.UnitPrice is > 0m &&
                                 lineSettlement == VisitChargesService.SettlementUnpaid &&
                                 !lineHasActivePending;

            var canSelectLine = !visitBlocked &&
                                !diagBlocked &&
                                line.UnitPrice is > 0m &&
                                lineSettlement == VisitChargesService.SettlementUnpaid;

            var canDeclineLine = !visitBlocked &&
                                 isOptionalOrder &&
                                 canExecuteTransition &&
                                 lineSettlement == VisitChargesService.SettlementUnpaid;

            var canSkipPaidLine = !visitBlocked &&
                                  isOptionalOrder &&
                                  canExecuteTransition &&
                                  lineSettlement == VisitChargesService.SettlementPaid;

            evaluatedLines.Add(line with
            {
                LineSettlementStatus = lineSettlement,
                PaymentId = latestLinePayment?.Id,
                PaymentCode = latestLinePayment?.PaymentCode,
                PaymentStatus = latestLinePaymentStatus ?? VisitChargesService.SettlementUnpaid,
                CanPayByQr = lineCanPayByQr,
                CanSelect = canSelectLine,
                CanDecline = canDeclineLine,
                CanSkipPaid = canSkipPaidLine,
            });
        }

        var activeRequiredLines = evaluatedLines
            .Where(l => IsStatus(l.ChargeStatus, VisitChargeStatuses.Active) && l.Payable == true && l.UnitPrice is > 0m)
            .ToList();

        var unpaidActiveLines = activeRequiredLines
            .Where(l => l.LineSettlementStatus == VisitChargesService.SettlementUnpaid)
            .ToList();

        string settlement;
        decimal? amountDue;

        if (hasUnpriced || hasInvalid || anyLineUndetermined)
        {
            settlement = VisitChargesService.SettlementUndetermined;
            amountDue = null;
        }
        else if (anyActiveLineMismatch && unpaidActiveLines.Count == 0)
        {
            settlement = VisitChargesService.SettlementMismatch;
            amountDue = null;
        }
        else if (activeRequiredLines.Count == 0)
        {
            if (anyCancelledRefundPending)
            {
                settlement = VisitChargesService.SettlementMismatch;
                amountDue = null;
            }
            else if (evaluatedLines.Any(l => l.LineSettlementStatus == LineSettlementPaidNotPerformed))
            {
                settlement = VisitChargesService.SettlementPaid;
                amountDue = 0m;
            }
            else if (paidAll.Count == 0)
            {
                settlement = VisitChargesService.SettlementUnpaid;
                amountDue = 0m;
            }
            else if (paidAll.Count == 1 && paidAll[0].Amount == 0m)
            {
                settlement = VisitChargesService.SettlementPaid;
                amountDue = 0m;
            }
            else
            {
                settlement = VisitChargesService.SettlementMismatch;
                amountDue = null;
            }
        }
        else if (unpaidActiveLines.Count == 0 &&
                 activeRequiredLines.All(l => l.LineSettlementStatus == VisitChargesService.SettlementPaid))
        {
            settlement = VisitChargesService.SettlementPaid;
            amountDue = 0m;
        }
        else if (unpaidActiveLines.Count > 0)
        {
            // PAY-17: Unrelated ACTIVE UNPAID lines remain payable even if another cancelled/mismatched line requires reconciliation!
            settlement = VisitChargesService.SettlementUnpaid;
            amountDue = unpaidActiveLines.Sum(l => l.UnitPrice!.Value);
        }
        else
        {
            settlement = VisitChargesService.SettlementMismatch;
            amountDue = null;
        }

        decimal? selectedAmountDue = amountDue is null
            ? null
            : unpaidActiveLines
                .Where(l => !IsStatus(l.SelectionStatus, DiagnosticSelectionStatuses.Undecided) &&
                            !IsStatus(l.SelectionStatus, DiagnosticSelectionStatuses.ReviewRequired))
                .Sum(l => l.UnitPrice!.Value);

        var latest = payments.FirstOrDefault();
        var effectivePaymentMethod = latest?.PaymentMethod ?? walkInPaymentMethod;

        var activePendingQr = payments.FirstOrDefault(p =>
            IsActivePending(p, staleCutoff) &&
            !string.Equals(p.PaymentMethod, PaymentMethods.Cash, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(p.PaymentCode));

        IReadOnlyList<long>? activePendingChargeIds = null;
        if (activePendingQr is not null)
        {
            var pendingChargeIds = effectiveAllocations
                .Where(a => a.PaymentId == activePendingQr.Id && IsStatus(a.Status, PaymentAllocationStatuses.Pending))
                .Select(a => a.VisitChargeId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            if (pendingChargeIds.Count > 0)
                activePendingChargeIds = pendingChargeIds;
        }

        var visitCanPayByQr = settlement == VisitChargesService.SettlementUnpaid &&
                              amountDue is > 0m &&
                              evaluatedLines.Any(l => l.CanPayByQr);

        return new VisitChargeAssessment(
            evaluatedLines,
            activePayableTotal,
            hasUnpriced,
            hasInvalid,
            amountDue,
            recognizedPaid,
            settlement,
            latest?.Id,
            latest?.PaymentCode,
            latest is null ? VisitChargesService.SettlementUnpaid : EffectivePaymentStatus(latest, staleCutoff),
            latest?.Amount,
            effectivePaymentMethod,
            latest?.PaidAt,
            latest?.ExternalReference,
            visitCanPayByQr,
            SelectedAmountDue: selectedAmountDue,
            ActivePendingQrPaymentId: activePendingQr?.Id,
            ActivePendingQrPaymentCode: activePendingQr?.PaymentCode,
            ActivePendingQrAmount: activePendingQr?.Amount,
            ActivePendingQrChargeIds: activePendingChargeIds,
            HasReconciliationItems: hasReconciliationItems);
    }

    private static async Task<VisitChargeAssessment> AssessDerivedLegacyVisitAsync(
        IApplicationDbContext db,
        bool visitBlocked,
        string? paymentMethod,
        DateTime staleCutoff,
        VisitWalkInAudits.WalkInAudit? audit,
        ScheduledPrice? scheduled,
        IReadOnlyList<Payment> payments,
        CancellationToken ct)
    {
        var derived = await MapDerivedAsync(db, audit, scheduled, ct);
        var lines = await FillServiceNamesAsync(db, [derived], ct);
        var single = lines[0];

        var payableTotal = single.Payable == true && single.UnitPrice is >= 0m
            ? single.UnitPrice.Value
            : 0m;

        var hasUnpriced = single.Payable is null || (single.Payable == true && single.UnitPrice is null);
        var hasInvalid = single.ChargeDataStatus == VisitChargesService.ChargeDataInvalid;

        var paid = DedupePaid(payments);
        var recognizedPaid = paid.Aggregate(0m, (sum, p) => sum + (p.Amount ?? 0m));
        var paidAmountMissing = paid.Any(p => p.Amount is null);
        var hasMismatchRow = payments.Any(p => IsStatus(p.Status, VisitChargesService.SettlementMismatch));

        string settlement;
        decimal? amountDue;
        if (hasUnpriced || hasInvalid || paidAmountMissing)
        {
            settlement = VisitChargesService.SettlementUndetermined;
            amountDue = null;
        }
        else if (hasMismatchRow)
        {
            settlement = VisitChargesService.SettlementMismatch;
            amountDue = null;
        }
        else if (paid.Count == 0)
        {
            settlement = VisitChargesService.SettlementUnpaid;
            amountDue = payableTotal;
        }
        else if (paid.Count == 1 && paid[0].Amount == payableTotal)
        {
            settlement = VisitChargesService.SettlementPaid;
            amountDue = 0m;
        }
        else
        {
            settlement = VisitChargesService.SettlementMismatch;
            amountDue = null;
        }

        var latest = payments.FirstOrDefault();
        var latestStatus = latest is null
            ? VisitChargesService.SettlementUnpaid
            : EffectivePaymentStatus(latest, staleCutoff);
        var hasActivePending = payments.Any(p => IsActivePending(p, staleCutoff));

        var canPayByQr = !visitBlocked &&
                         !hasUnpriced &&
                         !hasInvalid &&
                         settlement == VisitChargesService.SettlementUnpaid &&
                         amountDue is > 0m &&
                         !hasActivePending &&
                         !string.Equals(paymentMethod, "CASHIER", StringComparison.OrdinalIgnoreCase);

        var enrichedLine = single with
        {
            LineSettlementStatus = settlement,
            PaymentId = latest?.Id,
            PaymentCode = latest?.PaymentCode,
            PaymentStatus = latestStatus,
            CanPayByQr = canPayByQr,
            CanSelect = canPayByQr,
        };

        return new VisitChargeAssessment(
            [enrichedLine],
            payableTotal,
            hasUnpriced,
            hasInvalid,
            amountDue,
            recognizedPaid,
            settlement,
            latest?.Id,
            latest?.PaymentCode,
            latestStatus,
            latest?.Amount,
            latest?.PaymentMethod ?? paymentMethod,
            latest?.PaidAt,
            latest?.ExternalReference,
            canPayByQr,
            SelectedAmountDue: amountDue,
            HasReconciliationItems: settlement == VisitChargesService.SettlementMismatch);
    }

    private static VisitChargeLineDto MapPersistedBase(
        VisitCharge charge,
        IReadOnlyDictionary<long, DiagnosticOrder>? diagOrdersById)
    {
        var raw = string.IsNullOrWhiteSpace(charge.PaymentRequirement)
            ? null
            : charge.PaymentRequirement.Trim();
        var normalized = raw?.ToUpperInvariant();
        var negative = charge.UnitPrice is < 0;

        var chargeType = string.IsNullOrWhiteSpace(charge.ChargeType)
            ? (charge.DiagnosticOrderId.HasValue ? VisitChargeTypes.Diagnostic : VisitChargeTypes.Examination)
            : charge.ChargeType.Trim().ToUpperInvariant();

        var chargeStatus = string.IsNullOrWhiteSpace(charge.Status)
            ? VisitChargeStatuses.Active
            : charge.Status.Trim().ToUpperInvariant();

        DiagnosticOrder? diagOrder = null;
        if (charge.DiagnosticOrderId.HasValue &&
            diagOrdersById is not null &&
            diagOrdersById.TryGetValue(charge.DiagnosticOrderId.Value, out var foundOrder))
        {
            diagOrder = foundOrder;
            if (chargeStatus == VisitChargeStatuses.Active && IsStatus(diagOrder.Status, "CANCELLED"))
            {
                chargeStatus = IsStatus(diagOrder.SelectionStatus, DiagnosticSelectionStatuses.SkippedAfterPaid)
                    ? VisitChargeStatuses.PaidNotPerformed
                    : VisitChargeStatuses.Cancelled;
            }
        }

        bool? payable;
        string dataStatus;
        if (negative || (normalized is not null && normalized is not "REQUIRED" && normalized is not "WAIVED"))
        {
            payable = null;
            dataStatus = VisitChargesService.ChargeDataInvalid;
        }
        else if (normalized == "WAIVED")
        {
            payable = false;
            dataStatus = VisitChargesService.ChargeDataValid;
        }
        else if (charge.UnitPrice is null)
        {
            payable = true;
            dataStatus = VisitChargesService.ChargeDataUnpriced;
        }
        else
        {
            payable = true;
            dataStatus = VisitChargesService.ChargeDataValid;
        }

        var selectionStatus = diagOrder is not null
            ? (string.IsNullOrWhiteSpace(diagOrder.SelectionStatus)
                ? DiagnosticSelectionStatuses.Accepted
                : diagOrder.SelectionStatus.Trim().ToUpperInvariant())
            : (chargeType == VisitChargeTypes.Examination ? DiagnosticSelectionStatuses.Accepted : null);

        var requiredForConclusion = diagOrder is not null
            ? diagOrder.RequiredForConclusion ?? true
            : (chargeType == VisitChargeTypes.Examination ? true : (bool?)null);

        return new VisitChargeLineDto(
            charge.Id,
            VisitChargesService.ChargeSourcePersisted,
            charge.ServiceId,
            null,
            null,
            charge.DiagnosticOrderId,
            charge.UnitPrice,
            raw,
            payable,
            dataStatus,
            ChargeType: chargeType,
            ChargeStatus: chargeStatus,
            DiagnosticOrderCode: diagOrder?.OrderCode,
            RequiredForConclusion: requiredForConclusion,
            SelectionStatus: selectionStatus);
    }

    private static async Task<VisitChargeLineDto> MapDerivedAsync(
        IApplicationDbContext db,
        VisitWalkInAudits.WalkInAudit? audit,
        ScheduledPrice? scheduled,
        CancellationToken ct)
    {
        decimal? price = null;
        long? serviceId = null;
        string? serviceCode = null;
        string? serviceName = null;
        var invalidPrice = false;

        if (audit?.ServicePrice is decimal auditPrice)
        {
            serviceId = audit.ServiceId;
            serviceCode = audit.ServiceCode;
            serviceName = audit.ServiceName;
            price = auditPrice;
            invalidPrice = auditPrice < 0;
        }
        else if (scheduled is not null)
        {
            serviceId = scheduled.ServiceId;
            serviceCode = scheduled.ServiceCode;
            serviceName = scheduled.ServiceName;
            price = scheduled.Price;
            invalidPrice = scheduled.Price is < 0;
        }

        if (serviceId is long id && (serviceCode is null || serviceName is null))
        {
            var service = await db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
            serviceCode ??= service?.ServiceCode;
            serviceName ??= service?.Name;
        }

        var dataStatus = invalidPrice
            ? VisitChargesService.ChargeDataInvalid
            : price is null ? VisitChargesService.ChargeDataUnpriced : VisitChargesService.ChargeDataValid;

        return new VisitChargeLineDto(
            null,
            VisitChargesService.ChargeSourceDerived,
            serviceId,
            serviceCode,
            serviceName,
            null,
            price,
            null,
            invalidPrice ? null : true,
            dataStatus,
            ChargeType: VisitChargeTypes.Examination,
            ChargeStatus: VisitChargeStatuses.Active,
            RequiredForConclusion: true,
            SelectionStatus: DiagnosticSelectionStatuses.Accepted);
    }

    private static async Task<IReadOnlyDictionary<long, DiagnosticOrder>?> TryLoadDiagnosticOrdersAsync(
        IApplicationDbContext db,
        IReadOnlyList<VisitCharge> charges,
        CancellationToken ct)
    {
        var orderIds = charges
            .Where(c => c.DiagnosticOrderId.HasValue)
            .Select(c => c.DiagnosticOrderId!.Value)
            .Distinct()
            .ToList();

        if (orderIds.Count == 0)
            return null;

        try
        {
            return await db.DiagnosticOrders.AsNoTracking()
                .Where(d => orderIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, ct);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static async Task<List<VisitChargeLineDto>> FillServiceNamesAsync(
        IApplicationDbContext db,
        List<VisitChargeLineDto> lines,
        CancellationToken ct)
    {
        var missing = lines
            .Where(l => l.ServiceId is not null && (l.ServiceCode is null || l.ServiceName is null))
            .Select(l => l.ServiceId!.Value)
            .Distinct()
            .ToList();

        if (missing.Count == 0)
            return lines;

        var services = await db.Services.AsNoTracking()
            .Where(s => missing.Contains(s.Id))
            .ToListAsync(ct);

        return lines.Select(line =>
        {
            if (line.ServiceId is not long id)
                return line;
            var service = services.FirstOrDefault(s => s.Id == id);
            if (service is null)
                return line;
            return line with
            {
                ServiceCode = line.ServiceCode ?? service.ServiceCode,
                ServiceName = line.ServiceName ?? service.Name,
            };
        }).ToList();
    }

    private static async Task<ScheduledPrice?> LoadScheduledPriceAsync(
        IApplicationDbContext db,
        TimeProvider time,
        Visit visit,
        CancellationToken ct)
    {
        if (visit.AppointmentId is not long appointmentId)
            return null;

        var appointment = await db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment?.SlotId is not long slotId)
            return null;

        var slot = await db.ExaminationSlots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot?.ServiceId is not long serviceId)
            return null;

        var service = await db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceId, ct);
        var reference = visit.CheckedInAt ?? AppointmentBookingRules.GetVietnamNow(time);
        var amounts = await db.ServicePrices.AsNoTracking()
            .Where(p =>
                p.ServiceId == serviceId &&
                p.PriceAmount != null &&
                (p.EffectiveFrom == null || p.EffectiveFrom <= reference) &&
                (p.EffectiveTo == null || p.EffectiveTo >= reference))
            .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(p => p.Id)
            .Select(p => p.PriceAmount!.Value)
            .ToListAsync(ct);

        return new ScheduledPrice(
            serviceId,
            service?.ServiceCode,
            service?.Name,
            amounts.Count == 0 ? null : amounts[0]);
    }

    private static List<EffectiveChargeAllocation> DedupePaidAllocations(IEnumerable<EffectiveChargeAllocation> allocations) =>
        allocations
            .Where(a => IsStatus(a.Status, PaymentAllocationStatuses.Paid))
            .GroupBy(a => a.Payment is not null ? DedupeKey(a.Payment) : $"alloc:{a.PaymentId}:{a.VisitChargeId}", StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(a => a.PaymentId).First())
            .ToList();

    private static List<Payment> DedupePaid(IEnumerable<Payment> payments) =>
        payments
            .Where(p => IsStatus(p.Status, PaymentStatuses.Paid))
            .GroupBy(DedupeKey, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(p => p.Id).First())
            .ToList();

    private static bool IsOrderStartedOrCompleted(string? status)
    {
        var s = status?.Trim().ToUpperInvariant();
        return s is "IN_PROGRESS" or "COMPLETED" or "FINALIZED" or "VERIFIED" or "CANCELLED";
    }

    private static bool IsActivePending(Payment payment, DateTime staleCutoff) =>
        IsStatus(payment.Status, PaymentStatuses.Pending) &&
        (!payment.CreatedAt.HasValue || payment.CreatedAt.Value >= staleCutoff);

    private static string? EffectivePaymentStatus(Payment? payment, DateTime staleCutoff)
    {
        if (payment is null)
            return null;

        if (IsStatus(payment.Status, PaymentStatuses.Pending) &&
            payment.CreatedAt.HasValue &&
            payment.CreatedAt.Value < staleCutoff)
        {
            return PaymentStatuses.Expired;
        }

        return payment.Status;
    }

    private static string DedupeKey(Payment payment)
    {
        if (!string.IsNullOrWhiteSpace(payment.PaymentCode))
            return "code:" + payment.PaymentCode.Trim();
        if (!string.IsNullOrWhiteSpace(payment.ExternalReference))
            return "ref:" + payment.ExternalReference.Trim();
        return "id:" + payment.Id.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsStatus(string? status, string expected) =>
        string.Equals(status?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private sealed record ScheduledPrice(
        long ServiceId,
        string? ServiceCode,
        string? ServiceName,
        decimal? Price);
}
