using MedFlow.Domain.Constants;

namespace MedFlow.Domain.Entities;

public class VisitCharge
{
    public long Id { get; set; }
    public long? VisitId { get; set; }
    public long? ServiceId { get; set; }
    public long? DiagnosticOrderId { get; set; }
    /// <summary>Line amount. visit_charges has no quantity column.</summary>
    public decimal? UnitPrice { get; set; }
    public string? PaymentRequirement { get; set; }

    /// <summary>EXAMINATION or DIAGNOSTIC (ADR-0059).</summary>
    public string ChargeType { get; set; } = VisitChargeTypes.Examination;

    /// <summary>ACTIVE, CANCELLED, or CANCELLED_REFUND_PENDING.</summary>
    public string Status { get; set; } = VisitChargeStatuses.Active;

    public DateTime? CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public static VisitCharge CreateExamination(
        long visitId,
        long serviceId,
        decimal unitPrice,
        DateTime createdAtLocal,
        bool waived = false)
    {
        if (visitId <= 0)
            throw new ArgumentOutOfRangeException(nameof(visitId));
        if (serviceId <= 0)
            throw new ArgumentOutOfRangeException(nameof(serviceId));
        if (unitPrice < 0m)
            throw new ArgumentOutOfRangeException(nameof(unitPrice));

        return new VisitCharge
        {
            VisitId = visitId,
            ServiceId = serviceId,
            DiagnosticOrderId = null,
            ChargeType = VisitChargeTypes.Examination,
            UnitPrice = unitPrice,
            PaymentRequirement = (waived || unitPrice == 0m) ? "WAIVED" : "REQUIRED",
            Status = VisitChargeStatuses.Active,
            CreatedAt = createdAtLocal,
        };
    }

    public static VisitCharge CreateDiagnostic(
        long visitId,
        long diagnosticOrderId,
        long serviceId,
        decimal unitPrice,
        DateTime createdAtLocal,
        bool waived = false)
    {
        if (visitId <= 0)
            throw new ArgumentOutOfRangeException(nameof(visitId));
        if (diagnosticOrderId <= 0)
            throw new ArgumentOutOfRangeException(nameof(diagnosticOrderId));
        if (serviceId <= 0)
            throw new ArgumentOutOfRangeException(nameof(serviceId));
        if (unitPrice < 0m)
            throw new ArgumentOutOfRangeException(nameof(unitPrice));

        return new VisitCharge
        {
            VisitId = visitId,
            ServiceId = serviceId,
            DiagnosticOrderId = diagnosticOrderId,
            ChargeType = VisitChargeTypes.Diagnostic,
            UnitPrice = unitPrice,
            PaymentRequirement = (waived || unitPrice == 0m) ? "WAIVED" : "REQUIRED",
            Status = VisitChargeStatuses.Active,
            CreatedAt = createdAtLocal,
        };
    }
}
