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
    /// CHARGE (new per-charge Pay-Before-Service rows) or LEGACY_VISIT (pre-023 historical rows).
    /// Enforced at DB level via chk_payments_scope_charge_consistency and fk_payments_charge_visit.
    /// </summary>
    public string PaymentScope { get; set; } = PaymentScopes.LegacyVisit;

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
            Amount = amount,
            Status = status,
            CreatedAt = createdAtLocal,
        };
    }
}
