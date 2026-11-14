using System.Globalization;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
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

        List<VisitChargeLineDto> lines;
        if (charges.Count > 0)
            lines = charges.Select(MapPersisted).ToList();
        else
            lines = [await MapDerivedAsync(db, audit, scheduled, ct)];

        lines = await FillServiceNamesAsync(db, lines, ct);

        var payableTotal = lines
            .Where(l => l.Payable == true && l.UnitPrice is >= 0)
            .Sum(l => l.UnitPrice!.Value);

        var hasUnpriced = lines.Any(l =>
            l.Payable is null || (l.Payable == true && l.UnitPrice is null));
        var hasInvalid = lines.Any(l => l.ChargeDataStatus == VisitChargesService.ChargeDataInvalid);

        var paid = payments
            .Where(p => IsStatus(p.Status, "PAID"))
            .GroupBy(DedupeKey, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(p => p.Id).First())
            .ToList();

        var recognizedPaid = paid.Aggregate(0m, (sum, p) => sum + (p.Amount ?? 0m));
        var paidAmountMissing = paid.Any(p => p.Amount is null);

        string settlement;
        decimal? amountDue;
        if (hasUnpriced || paidAmountMissing)
        {
            settlement = VisitChargesService.SettlementUndetermined;
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
        var paymentMethod = VisitWalkInAudits.NormalizePaymentMethod(audit?.PaymentMethod);
        var hasPending = payments.Any(p => IsStatus(p.Status, "PENDING"));
        var visitBlocked = IsStatus(visit.Status, "CANCELLED") || IsStatus(visit.Status, "NO_SHOW");

        var canPayByQr = !visitBlocked
            && !hasUnpriced
            && !hasInvalid
            && settlement == VisitChargesService.SettlementUnpaid
            && amountDue is > 0
            && !hasPending
            && !string.Equals(paymentMethod, "CASHIER", StringComparison.OrdinalIgnoreCase);

        return new VisitChargeAssessment(
            lines,
            payableTotal,
            hasUnpriced,
            hasInvalid,
            amountDue,
            recognizedPaid,
            settlement,
            latest?.Id,
            latest?.PaymentCode,
            latest is null ? VisitChargesService.SettlementUnpaid : latest.Status,
            latest?.Amount,
            paymentMethod,
            latest?.PaidAt,
            latest?.ExternalReference,
            canPayByQr);
    }

    private static VisitChargeLineDto MapPersisted(VisitCharge charge)
    {
        var raw = string.IsNullOrWhiteSpace(charge.PaymentRequirement)
            ? null
            : charge.PaymentRequirement.Trim();
        var normalized = raw?.ToUpperInvariant();
        var negative = charge.UnitPrice is < 0;

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
            dataStatus);
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
            dataStatus);
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
