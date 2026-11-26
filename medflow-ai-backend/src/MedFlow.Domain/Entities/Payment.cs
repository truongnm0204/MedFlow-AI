using MedFlow.Domain.Constants;

namespace MedFlow.Domain.Entities;

public class Payment
{
    public long Id { get; set; }
    public string? PaymentCode { get; set; }
    public long? VisitId { get; set; }
    public long? VisitChargeId { get; set; }
    public decimal? Amount { get; set; }
    public string? ExternalReference { get; set; }
    public string? Status { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// CHARGE (ADR-0059 single-charge), ALLOCATED (ADR-0060 1..N payment_allocations), or LEGACY_VISIT (pre-023 historical rows).
    /// Enforced at DB level via chk_payments_scope_charge_consistency and fk_payments_charge_visit.
    /// </summary>
    public string PaymentScope { get; set; } = PaymentScopes.LegacyVisit;

    public string? PaymentMethod { get; set; }
    public long? CollectedByStaffId { get; set; }
    public string? IdempotencyKey { get; set; }

    public static Payment CreateForCharge(
        long visitId,
        long visitChargeId,
        decimal amount,
        DateTime createdAtLocal,
        string status = "PENDING")
    {
        if (visitId <= 0)
            throw new ArgumentOutOfRangeException(nameof(visitId));
        if (visitChargeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(visitChargeId));
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount));

        return new Payment
        {
            VisitId = visitId,
            VisitChargeId = visitChargeId,
            PaymentScope = PaymentScopes.Charge,
            PaymentMethod = PaymentMethods.VietQr,
            Amount = amount,
            Status = status,
            CreatedAt = createdAtLocal,
        };
    }

    public static Payment CreateAllocatedQr(
        long visitId,
        long? singleVisitChargeId,
        decimal totalAmount,
        DateTime createdAtLocal,
        string? idempotencyKey = null,
        string status = "PENDING")
    {
        if (visitId <= 0)
            throw new ArgumentOutOfRangeException(nameof(visitId));
        if (singleVisitChargeId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(singleVisitChargeId));
        if (totalAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(totalAmount));

        return new Payment
        {
            VisitId = visitId,
            VisitChargeId = singleVisitChargeId,
            PaymentScope = PaymentScopes.Allocated,
            PaymentMethod = PaymentMethods.VietQr,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            Amount = totalAmount,
            Status = status,
            CreatedAt = createdAtLocal,
        };
    }

    public static Payment CreateCash(
        long visitId,
        long? singleVisitChargeId,
        decimal totalAmount,
        long collectedByStaffId,
        string idempotencyKey,
        DateTime paidAtLocal)
    {
        if (visitId <= 0)
            throw new ArgumentOutOfRangeException(nameof(visitId));
        if (singleVisitChargeId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(singleVisitChargeId));
        if (totalAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(totalAmount));
        if (collectedByStaffId <= 0)
            throw new ArgumentOutOfRangeException(nameof(collectedByStaffId));
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        return new Payment
        {
            VisitId = visitId,
            VisitChargeId = singleVisitChargeId,
            PaymentScope = PaymentScopes.Allocated,
            PaymentMethod = PaymentMethods.Cash,
            CollectedByStaffId = collectedByStaffId,
            IdempotencyKey = idempotencyKey.Trim(),
            Amount = totalAmount,
            Status = "PAID",
            CreatedAt = paidAtLocal,
            PaidAt = paidAtLocal,
        };
    }
}
