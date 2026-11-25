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
    bool CanPayByQr);

internal static class VisitChargeAssessor
{
    public const string LineSettlementWaived = "WAIVED";
    public const string LineSettlementCancelled = "CANCELLED";

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

        var paymentMethod = VisitWalkInAudits.NormalizePaymentMethod(audit?.PaymentMethod);
        var visitBlocked = IsStatus(visit.Status, "CANCELLED") || IsStatus(visit.Status, "NO_SHOW");
        var staleCutoff = HospitalClock.GetNow(time).AddMinutes(-GenerateQrPaymentService.PendingExpiryMinutes);

        if (charges.Count == 0)
        {
            return await AssessDerivedLegacyVisitAsync(
                db,
                visitBlocked,
                paymentMethod,
                staleCutoff,
                audit,
                scheduled,
                payments,
                ct);
        }

        var diagOrdersById = await TryLoadDiagnosticOrdersAsync(db, charges, ct);

        var paidAll = DedupePaid(payments);
        var recognizedPaid = paidAll.Aggregate(0m, (sum, p) => sum + (p.Amount ?? 0m));
        var paidAmountMissing = paidAll.Any(p => p.Amount is null);

        var chargeScopedPayments = payments.Where(p => p.VisitChargeId.HasValue).ToList();
        var legacyPayments = payments.Where(p => !p.VisitChargeId.HasValue).ToList();
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

        var isLegacyFullVisitPaid =
            chargeScopedPayments.Count == 0 &&
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
            !chargeScopedPayments.Any(p => p.VisitChargeId == activeExamLine.ChargeId) &&
            legacyPaid[0].Amount == activeExamLine.UnitPrice;

        var isLegacyMismatchWithoutChargePayments =
            chargeScopedPayments.Count == 0 &&
            !isLegacyFullVisitPaid &&
            !isLegacyExamOnlyPaid &&
            (hasLegacyMismatch || legacyPaid.Count > 0);

        var evaluatedLines = new List<VisitChargeLineDto>(rawLines.Count);
        var anyLineMismatch = isLegacyMismatchWithoutChargePayments;
        var anyLineUndetermined = paidAmountMissing;

        foreach (var line in rawLines)
        {
            var lineChargePayments = line.ChargeId.HasValue
                ? chargeScopedPayments.Where(p => p.VisitChargeId == line.ChargeId.Value).ToList()
                : [];

            var latestLinePayment = lineChargePayments.FirstOrDefault()
                ?? (chargeScopedPayments.Count == 0 &&
                    (rawLines.Count == 1 || IsStatus(line.ChargeType, VisitChargeTypes.Examination))
                        ? legacyPayments.FirstOrDefault()
                        : null);

            var latestLinePaymentStatus = EffectivePaymentStatus(latestLinePayment, staleCutoff);

            if (IsStatus(line.ChargeStatus, VisitChargeStatuses.CancelledRefundPending))
            {
                anyLineMismatch = true;
                evaluatedLines.Add(line with
                {
                    Payable = false,
                    LineSettlementStatus = VisitChargesService.SettlementMismatch,
                    PaymentId = latestLinePayment?.Id,
                    PaymentCode = latestLinePayment?.PaymentCode,
                    PaymentStatus = latestLinePaymentStatus,
                    CanPayByQr = false,
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
                });
                continue;
            }

            // Active payable line with UnitPrice > 0
            string lineSettlement;
            var lineHasActivePending = lineChargePayments.Any(p => IsActivePending(p, staleCutoff)) ||
                                       (chargeScopedPayments.Count == 0 && hasActiveLegacyPending);

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
                var linePaid = DedupePaid(lineChargePayments);
                var lineMismatchRow = lineChargePayments.Any(p => IsStatus(p.Status, VisitChargesService.SettlementMismatch));

                if (linePaid.Any(p => p.Amount is null))
                {
                    lineSettlement = VisitChargesService.SettlementUndetermined;
                    anyLineUndetermined = true;
                }
                else if (lineMismatchRow)
                {
                    lineSettlement = VisitChargesService.SettlementMismatch;
                    anyLineMismatch = true;
                }
                else if (linePaid.Count == 1 && linePaid[0].Amount == line.UnitPrice)
                {
                    lineSettlement = VisitChargesService.SettlementPaid;
                }
                else if (linePaid.Count > 0)
                {
                    lineSettlement = VisitChargesService.SettlementMismatch;
                    anyLineMismatch = true;
                }
                else
                {
                    lineSettlement = VisitChargesService.SettlementUnpaid;
                }
            }

            var diagBlocked = line.DiagnosticOrderId.HasValue &&
                              diagOrdersById is not null &&
                              (!diagOrdersById.TryGetValue(line.DiagnosticOrderId.Value, out var order) ||
                               IsStatus(order.Status, "CANCELLED"));

            var cashierBlocked = IsStatus(line.ChargeType, VisitChargeTypes.Examination) &&
                                 string.Equals(paymentMethod, "CASHIER", StringComparison.OrdinalIgnoreCase);

            var lineCanPayByQr = !visitBlocked &&
                                 !diagBlocked &&
                                 !cashierBlocked &&
                                 !hasUnpriced &&
                                 !hasInvalid &&
                                 line.UnitPrice is > 0m &&
                                 lineSettlement == VisitChargesService.SettlementUnpaid &&
                                 !lineHasActivePending;

            evaluatedLines.Add(line with
            {
                LineSettlementStatus = lineSettlement,
                PaymentId = latestLinePayment?.Id,
                PaymentCode = latestLinePayment?.PaymentCode,
                PaymentStatus = latestLinePaymentStatus ?? VisitChargesService.SettlementUnpaid,
                CanPayByQr = lineCanPayByQr,
            });
        }

        string settlement;
        decimal? amountDue;

        if (hasUnpriced || hasInvalid || anyLineUndetermined)
        {
            settlement = VisitChargesService.SettlementUndetermined;
            amountDue = null;
        }
        else if (anyLineMismatch)
        {
            settlement = VisitChargesService.SettlementMismatch;
            amountDue = null;
        }
        else
        {
            var activeRequiredLines = evaluatedLines
                .Where(l => IsStatus(l.ChargeStatus, VisitChargeStatuses.Active) && l.Payable == true && l.UnitPrice is > 0m)
                .ToList();

            if (activeRequiredLines.Count == 0)
            {
                if (paidAll.Count == 0)
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
            else
            {
                var unpaidRequiredLines = activeRequiredLines
                    .Where(l => l.LineSettlementStatus == VisitChargesService.SettlementUnpaid)
                    .ToList();

                if (unpaidRequiredLines.Count == 0 &&
                    activeRequiredLines.All(l => l.LineSettlementStatus == VisitChargesService.SettlementPaid))
                {
                    settlement = VisitChargesService.SettlementPaid;
                    amountDue = 0m;
                }
                else
                {
                    settlement = VisitChargesService.SettlementUnpaid;
                    amountDue = unpaidRequiredLines.Sum(l => l.UnitPrice!.Value);
                }
            }
        }

        var latest = payments.FirstOrDefault();
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
            paymentMethod,
            latest?.PaidAt,
            latest?.ExternalReference,
            visitCanPayByQr);
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
            paymentMethod,
            latest?.PaidAt,
            latest?.ExternalReference,
            canPayByQr);
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

        if (chargeStatus == VisitChargeStatuses.Active &&
            charge.DiagnosticOrderId.HasValue &&
            diagOrdersById is not null &&
            diagOrdersById.TryGetValue(charge.DiagnosticOrderId.Value, out var diagOrder) &&
            IsStatus(diagOrder.Status, "CANCELLED"))
        {
            chargeStatus = VisitChargeStatuses.Cancelled;
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
            ChargeStatus: chargeStatus);
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
            ChargeStatus: VisitChargeStatuses.Active);
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

    private static List<Payment> DedupePaid(IEnumerable<Payment> payments) =>
        payments
            .Where(p => IsStatus(p.Status, PaymentStatuses.Paid))
            .GroupBy(DedupeKey, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(p => p.Id).First())
            .ToList();

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
