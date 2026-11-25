using System.Globalization;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

/// <summary>
/// Per-Charge Pay-Before-Service eligibility reader (ADR-0059 Option A &amp; ADR-0047 v4.1).
/// Evaluates EXAMINATION charges, DIAGNOSTIC order charges, and Pharmacy visit clearance
/// without ever treating UNPRICED/INVALID/MISMATCH charges as free or inferring medicine retail prices.
/// </summary>
public sealed class VisitChargeSettlementReader : IVisitChargeSettlementReader
{
    public const string StatusPaid = "PAID";
    public const string StatusPaidLegacy = "PAID_LEGACY";
    public const string StatusWaived = "WAIVED";
    public const string StatusUnpaid = "UNPAID";
    public const string StatusUnpriced = "UNPRICED";
    public const string StatusInvalidCharge = "INVALID_CHARGE";
    public const string StatusMismatch = "MISMATCH";
    public const string StatusVisitNotFound = "VISIT_NOT_FOUND";
    public const string StatusVisitClosed = "VISIT_CLOSED";
    public const string StatusOrderNotFound = "ORDER_NOT_FOUND";
    public const string StatusOrderCancelled = "ORDER_CANCELLED";
    public const string StatusChargeCancelled = "CHARGE_CANCELLED";
    public const string StatusMissingCharge = "MISSING_CHARGE";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public VisitChargeSettlementReader(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<VisitChargeGateResult> CheckExaminationEligibilityAsync(
        long visitId,
        CancellationToken ct = default)
    {
        if (visitId <= 0)
        {
            return new VisitChargeGateResult(
                false,
                StatusVisitNotFound,
                null,
                null,
                "Không tìm thấy lượt khám.");
        }

        var visit = await _db.Visits.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == visitId, ct);

        if (visit is null)
        {
            return new VisitChargeGateResult(
                false,
                StatusVisitNotFound,
                null,
                null,
                "Không tìm thấy lượt khám.");
        }

        if (IsVisitClosed(visit.Status))
        {
            return new VisitChargeGateResult(
                false,
                StatusVisitClosed,
                null,
                null,
                "Lượt khám đã hủy hoặc không đến.");
        }

        var charges = await TryLoadVisitChargesAsync(visitId, ct);
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.VisitId == visitId)
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);

        if (charges is not null && charges.Count > 0)
        {
            var examCharges = charges
                .Where(IsExaminationCharge)
                .ToList();

            var activeExam = examCharges.FirstOrDefault(c => IsChargeActive(c.Status));
            if (activeExam is null)
            {
                var cancelledExam = examCharges.FirstOrDefault();
                if (cancelledExam is not null)
                {
                    var code = IsStatus(cancelledExam.Status, VisitChargeStatuses.CancelledRefundPending)
                        ? StatusMismatch
                        : StatusChargeCancelled;
                    return new VisitChargeGateResult(
                        false,
                        code,
                        cancelledExam.Id,
                        cancelledExam.UnitPrice,
                        "Khoản phí khám đã bị hủy hoặc đang chờ hoàn tiền.");
                }

                return new VisitChargeGateResult(
                    false,
                    StatusMissingCharge,
                    null,
                    null,
                    "Lượt khám chưa có khoản phí khám hợp lệ.");
            }

            return EvaluatePersistedCharge(activeExam, charges, payments, allowLegacyFallback: true);
        }

        return await EvaluateLegacyDerivedVisitAsync(visit, payments, forPharmacyFallback: false, ct);
    }

    public async Task<VisitChargeGateResult> CheckDiagnosticOrderEligibilityAsync(
        long diagnosticOrderId,
        CancellationToken ct = default)
    {
        if (diagnosticOrderId <= 0)
        {
            return new VisitChargeGateResult(
                false,
                StatusOrderNotFound,
                null,
                null,
                "Không tìm thấy chỉ định cận lâm sàng.");
        }

        var order = await _db.DiagnosticOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == diagnosticOrderId, ct);

        if (order is null)
        {
            return new VisitChargeGateResult(
                false,
                StatusOrderNotFound,
                null,
                null,
                "Không tìm thấy chỉ định cận lâm sàng.");
        }

        if (IsStatus(order.Status, "CANCELLED"))
        {
            return new VisitChargeGateResult(
                false,
                StatusOrderCancelled,
                null,
                null,
                "Chỉ định cận lâm sàng đã bị hủy.");
        }

        if (!order.VisitId.HasValue)
        {
            return new VisitChargeGateResult(
                false,
                StatusVisitNotFound,
                null,
                null,
                "Chỉ định cận lâm sàng chưa gắn lượt khám hợp lệ.");
        }

        var visit = await _db.Visits.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == order.VisitId.Value, ct);

        if (visit is null)
        {
            return new VisitChargeGateResult(
                false,
                StatusVisitNotFound,
                null,
                null,
                "Không tìm thấy lượt khám của chỉ định cận lâm sàng.");
        }

        if (IsVisitClosed(visit.Status))
        {
            return new VisitChargeGateResult(
                false,
                StatusVisitClosed,
                null,
                null,
                "Lượt khám đã hủy hoặc không đến.");
        }

        var charges = await _db.VisitCharges.AsNoTracking()
            .Where(c => c.VisitId == visit.Id)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        var diagCharge = charges.FirstOrDefault(c => c.DiagnosticOrderId == diagnosticOrderId);
        if (diagCharge is null)
        {
            return new VisitChargeGateResult(
                false,
                StatusMissingCharge,
                null,
                null,
                "Chỉ định cận lâm sàng chưa có khoản phí hợp lệ.");
        }

        if (!IsChargeActive(diagCharge.Status))
        {
            var code = IsStatus(diagCharge.Status, VisitChargeStatuses.CancelledRefundPending)
                ? StatusMismatch
                : StatusChargeCancelled;
            return new VisitChargeGateResult(
                false,
                code,
                diagCharge.Id,
                diagCharge.UnitPrice,
                "Khoản phí cận lâm sàng đã bị hủy hoặc đang chờ hoàn tiền.");
        }

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.VisitId == visit.Id)
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);

        return EvaluatePersistedCharge(diagCharge, charges, payments, allowLegacyFallback: true);
    }

    public async Task<VisitChargeGateResult> CheckPharmacyVisitClearanceAsync(
        long visitId,
        CancellationToken ct = default)
    {
        if (visitId <= 0)
        {
            return new VisitChargeGateResult(
                false,
                StatusVisitNotFound,
                null,
                null,
                "Không tìm thấy lượt khám.");
        }

        var visit = await _db.Visits.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == visitId, ct);

        if (visit is null)
        {
            return new VisitChargeGateResult(
                false,
                StatusVisitNotFound,
                null,
                null,
                "Không tìm thấy lượt khám.");
        }

        if (IsVisitClosed(visit.Status))
        {
            return new VisitChargeGateResult(
                false,
                StatusVisitClosed,
                null,
                null,
                "Lượt khám đã hủy hoặc không đến, không thể cấp phát thuốc.");
        }

        var charges = await TryLoadVisitChargesAsync(visitId, ct);
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.VisitId == visitId)
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);

        if (charges is not null && charges.Count > 0)
        {
            var examCharges = charges.Where(IsExaminationCharge).ToList();
            var activeExam = examCharges.FirstOrDefault(c => IsChargeActive(c.Status));
            if (activeExam is null)
            {
                var cancelledExam = examCharges.FirstOrDefault();
                return new VisitChargeGateResult(
                    false,
                    cancelledExam is not null && IsStatus(cancelledExam.Status, VisitChargeStatuses.CancelledRefundPending)
                        ? StatusMismatch
                        : StatusChargeCancelled,
                    cancelledExam?.Id,
                    cancelledExam?.UnitPrice,
                    "Khoản phí khám của lượt khám đã bị hủy hoặc đang chờ đối soát.");
            }

            return EvaluatePersistedCharge(activeExam, charges, payments, allowLegacyFallback: true);
        }

        return await EvaluateLegacyDerivedVisitAsync(visit, payments, forPharmacyFallback: true, ct);
    }

    private static VisitChargeGateResult EvaluatePersistedCharge(
        VisitCharge charge,
        IReadOnlyList<VisitCharge> allVisitCharges,
        IReadOnlyList<Payment> allVisitPayments,
        bool allowLegacyFallback)
    {
        var req = string.IsNullOrWhiteSpace(charge.PaymentRequirement)
            ? null
            : charge.PaymentRequirement.Trim().ToUpperInvariant();

        if (charge.UnitPrice is < 0m ||
            (req is not null && req is not "REQUIRED" && req is not "WAIVED"))
        {
            return new VisitChargeGateResult(
                false,
                StatusInvalidCharge,
                charge.Id,
                charge.UnitPrice,
                "Dữ liệu khoản phí không hợp lệ, vui lòng liên hệ thu ngân.");
        }

        if (req == "WAIVED" || charge.UnitPrice == 0m)
        {
            return new VisitChargeGateResult(
                true,
                StatusWaived,
                charge.Id,
                charge.UnitPrice ?? 0m);
        }

        if (charge.UnitPrice is null)
        {
            return new VisitChargeGateResult(
                false,
                StatusUnpriced,
                charge.Id,
                null,
                "Khoản phí chưa được cấu hình giá, không thể mở khóa dịch vụ.");
        }

        var expectedAmount = charge.UnitPrice.Value;
        var chargePayments = allVisitPayments
            .Where(p => p.VisitChargeId == charge.Id)
            .ToList();

        if (chargePayments.Any(p => IsStatus(p.Status, VisitChargesService.SettlementMismatch)))
        {
            return new VisitChargeGateResult(
                false,
                StatusMismatch,
                charge.Id,
                expectedAmount,
                "Khoản thanh toán đang lệch số tiền và chờ đối soát.");
        }

        var paidCharge = DedupePaid(chargePayments);
        if (paidCharge.Any(p => p.Amount is null))
        {
            return new VisitChargeGateResult(
                false,
                StatusMismatch,
                charge.Id,
                expectedAmount,
                "Dữ liệu thanh toán thiếu số tiền đối soát.");
        }

        if (paidCharge.Count == 1 &&
            paidCharge[0].Amount == expectedAmount &&
            string.Equals(paidCharge[0].PaymentScope, PaymentScopes.Charge, StringComparison.OrdinalIgnoreCase))
        {
            return new VisitChargeGateResult(
                true,
                StatusPaid,
                charge.Id,
                expectedAmount);
        }

        if (paidCharge.Count > 0)
        {
            return new VisitChargeGateResult(
                false,
                StatusMismatch,
                charge.Id,
                expectedAmount,
                "Số tiền đã thanh toán không khớp khoản phí yêu cầu.");
        }

        if (allowLegacyFallback && chargePayments.Count == 0)
        {
            var legacyPayments = allVisitPayments
                .Where(p => p.VisitChargeId is null)
                .ToList();

            if (legacyPayments.Any(p => IsStatus(p.Status, VisitChargesService.SettlementMismatch)))
            {
                return new VisitChargeGateResult(
                    false,
                    StatusMismatch,
                    charge.Id,
                    expectedAmount,
                    "Khoản thanh toán lượt khám đang chờ đối soát.");
            }

            var legacyPaid = DedupePaid(legacyPayments);
            if (legacyPaid.Count == 1 && legacyPaid[0].Amount is decimal legacyPaidAmount)
            {
                var allActivePayableTotal = allVisitCharges
                    .Where(c => IsChargeActive(c.Status) && IsPayableCharge(c) && c.UnitPrice is > 0m)
                    .Sum(c => c.UnitPrice!.Value);

                var anyChargeScopedPayments = allVisitPayments.Any(p => p.VisitChargeId.HasValue);

                if (!anyChargeScopedPayments && legacyPaidAmount == allActivePayableTotal)
                {
                    return new VisitChargeGateResult(
                        true,
                        StatusPaidLegacy,
                        charge.Id,
                        expectedAmount);
                }

                if (IsExaminationCharge(charge) && legacyPaidAmount == expectedAmount)
                {
                    return new VisitChargeGateResult(
                        true,
                        StatusPaidLegacy,
                        charge.Id,
                        expectedAmount);
                }

                if (IsExaminationCharge(charge))
                {
                    return new VisitChargeGateResult(
                        false,
                        StatusMismatch,
                        charge.Id,
                        expectedAmount,
                        "Khoản thanh toán lịch sử không khớp phí khám.");
                }
            }
            else if (legacyPaid.Count > 1 && IsExaminationCharge(charge))
            {
                return new VisitChargeGateResult(
                    false,
                    StatusMismatch,
                    charge.Id,
                    expectedAmount,
                    "Lượt khám có nhiều giao dịch thanh toán lịch sử cần đối soát.");
            }
        }

        return new VisitChargeGateResult(
            false,
            StatusUnpaid,
            charge.Id,
            expectedAmount,
            "Chưa hoàn tất thanh toán khoản phí trước khi thực hiện dịch vụ.");
    }

    private async Task<VisitChargeGateResult> EvaluateLegacyDerivedVisitAsync(
        Visit visit,
        IReadOnlyList<Payment> payments,
        bool forPharmacyFallback,
        CancellationToken ct)
    {
        if (payments.Any(p => IsStatus(p.Status, VisitChargesService.SettlementMismatch)))
        {
            return new VisitChargeGateResult(
                false,
                StatusMismatch,
                null,
                null,
                "Khoản thanh toán của lượt khám đang chờ đối soát.");
        }

        var derivedPrice = await TryLoadLegacyDerivedPriceAsync(visit, ct);
        if (derivedPrice.IsInvalid)
        {
            return new VisitChargeGateResult(
                false,
                StatusInvalidCharge,
                null,
                derivedPrice.Price,
                "Dữ liệu phí khám của lượt khám không hợp lệ.");
        }

        var paid = DedupePaid(payments);
        if (paid.Any(p => p.Amount is null))
        {
            return new VisitChargeGateResult(
                false,
                StatusMismatch,
                null,
                derivedPrice.Price,
                "Dữ liệu thanh toán thiếu số tiền đối soát.");
        }

        if (derivedPrice.HasPriceSource)
        {
            if (derivedPrice.Price is null)
            {
                return new VisitChargeGateResult(
                    false,
                    StatusUnpriced,
                    null,
                    null,
                    "Khoản phí khám chưa được cấu hình giá.");
            }

            if (derivedPrice.Price.Value == 0m)
            {
                return new VisitChargeGateResult(true, StatusWaived, null, 0m);
            }

            if (paid.Count == 1 && paid[0].Amount == derivedPrice.Price.Value)
            {
                return new VisitChargeGateResult(true, StatusPaidLegacy, null, derivedPrice.Price.Value);
            }

            if (paid.Count > 0)
            {
                return new VisitChargeGateResult(
                    false,
                    StatusMismatch,
                    null,
                    derivedPrice.Price.Value,
                    "Số tiền đã thanh toán không khớp phí khám.");
            }

            return new VisitChargeGateResult(
                false,
                StatusUnpaid,
                null,
                derivedPrice.Price.Value,
                "Lượt khám chưa hoàn tất thanh toán phí khám trước khi thực hiện dịch vụ.");
        }

        if (paid.Count == 1 && paid[0].Amount is > 0m)
        {
            return new VisitChargeGateResult(true, StatusPaidLegacy, null, paid[0].Amount);
        }

        if (paid.Count > 1)
        {
            return new VisitChargeGateResult(
                false,
                StatusMismatch,
                null,
                null,
                "Lượt khám có nhiều giao dịch thanh toán cần đối soát.");
        }

        if (payments.Count > 0)
        {
            return new VisitChargeGateResult(
                false,
                StatusUnpaid,
                null,
                payments[0].Amount,
                "Lượt khám chưa hoàn tất thanh toán. Vui lòng thanh toán trước khi cấp phát thuốc.");
        }

        if (forPharmacyFallback)
        {
            return new VisitChargeGateResult(
                false,
                StatusUnpriced,
                null,
                null,
                "Lượt khám chưa có khoản phí khám hoặc xác nhận miễn phí hợp lệ.");
        }

        return new VisitChargeGateResult(
            false,
            StatusUnpriced,
            null,
            null,
            "Chưa xác định được khoản phí khám của lượt khám.");
    }

    private async Task<List<VisitCharge>?> TryLoadVisitChargesAsync(long visitId, CancellationToken ct)
    {
        try
        {
            return await _db.VisitCharges.AsNoTracking()
                .Where(c => c.VisitId == visitId)
                .OrderBy(c => c.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<(bool HasPriceSource, decimal? Price, bool IsInvalid)> TryLoadLegacyDerivedPriceAsync(
        Visit visit,
        CancellationToken ct)
    {
        try
        {
            var audits = await VisitWalkInAudits.LoadAsync(_db, visit.Id, ct);
            var audit = audits.FirstOrDefault();
            if (audit is not null)
            {
                if (audit.ServicePrice is < 0m)
                    return (true, audit.ServicePrice, true);
                return (true, audit.ServicePrice, false);
            }
        }
        catch (NotSupportedException)
        {
            // Minimal test double without AuditLogs DbSet.
        }

        if (visit.AppointmentId is long appointmentId)
        {
            try
            {
                var appointment = await _db.Appointments.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
                if (appointment?.SlotId is not long slotId)
                    return (true, null, false);

                var slot = await _db.ExaminationSlots.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == slotId, ct);
                if (slot?.ServiceId is not long serviceId)
                    return (true, null, false);

                var reference = visit.CheckedInAt ?? AppointmentBookingRules.GetVietnamNow(_timeProvider);
                var amounts = await _db.ServicePrices.AsNoTracking()
                    .Where(p =>
                        p.ServiceId == serviceId &&
                        p.PriceAmount != null &&
                        (p.EffectiveFrom == null || p.EffectiveFrom <= reference) &&
                        (p.EffectiveTo == null || p.EffectiveTo >= reference))
                    .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
                    .ThenByDescending(p => p.Id)
                    .Select(p => p.PriceAmount!.Value)
                    .ToListAsync(ct);

                if (amounts.Count == 0)
                    return (true, null, false);
                if (amounts[0] < 0m)
                    return (true, amounts[0], true);
                return (true, amounts[0], false);
            }
            catch (NotSupportedException)
            {
                return (true, null, false);
            }
        }

        return (false, null, false);
    }

    private static List<Payment> DedupePaid(IEnumerable<Payment> payments) =>
        payments
            .Where(p => IsStatus(p.Status, PaymentStatuses.Paid))
            .GroupBy(DedupeKey, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(p => p.Id).First())
            .ToList();

    private static string DedupeKey(Payment payment)
    {
        if (!string.IsNullOrWhiteSpace(payment.PaymentCode))
            return "code:" + payment.PaymentCode.Trim();
        if (!string.IsNullOrWhiteSpace(payment.ExternalReference))
            return "ref:" + payment.ExternalReference.Trim();
        return "id:" + payment.Id.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsExaminationCharge(VisitCharge charge) =>
        string.IsNullOrWhiteSpace(charge.ChargeType)
            ? !charge.DiagnosticOrderId.HasValue
            : string.Equals(charge.ChargeType.Trim(), VisitChargeTypes.Examination, StringComparison.OrdinalIgnoreCase);

    private static bool IsChargeActive(string? status) =>
        string.IsNullOrWhiteSpace(status) ||
        string.Equals(status.Trim(), VisitChargeStatuses.Active, StringComparison.OrdinalIgnoreCase);

    private static bool IsPayableCharge(VisitCharge charge)
    {
        var req = charge.PaymentRequirement?.Trim().ToUpperInvariant();
        if (charge.UnitPrice is < 0m || (req is not null && req is not "REQUIRED" && req is not "WAIVED"))
            return false;
        return req != "WAIVED";
    }

    private static bool IsVisitClosed(string? status) =>
        IsStatus(status, "CANCELLED") || IsStatus(status, "NO_SHOW");

    private static bool IsStatus(string? status, string expected) =>
        string.Equals(status?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
