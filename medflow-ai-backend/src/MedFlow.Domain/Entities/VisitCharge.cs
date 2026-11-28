using MedFlow.Domain.Constants;

namespace MedFlow.Domain.Entities;

public class VisitCharge
{
    public long Id { get; set; }
    public long? VisitId { get; set; }
    public long? ServiceId { get; set; }
    public long? DiagnosticOrderId { get; set; }
    /// <summary>
    /// Line amount. For MEDICINE charges this is the rounded line total
    /// (<see cref="MedicineUnitPrice"/> × <see cref="Quantity"/>); <see cref="MedicineUnitPrice"/> keeps the snapshot đơn giá.
    /// </summary>
    public decimal? UnitPrice { get; set; }
    public string? PaymentRequirement { get; set; }

    /// <summary>Prescription that generated this MEDICINE charge.</summary>
    public long? PrescriptionId { get; set; }

    /// <summary>Prescription line that generated this MEDICINE charge (one charge per line).</summary>
    public long? PrescriptionItemId { get; set; }

    public long? MedicineId { get; set; }

    /// <summary>Prescribed quantity snapshot for a MEDICINE charge (numeric(14,3)).</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Effective per-unit medicine price snapshot (numeric(14,2)) at charge creation time.</summary>
    public decimal? MedicineUnitPrice { get; set; }

    /// <summary>EXAMINATION, DIAGNOSTIC or MEDICINE (ADR-0059 / #866).</summary>
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

    /// <summary>
    /// One MEDICINE charge per prescription line. <paramref name="medicineUnitPrice"/> is the effective
    /// per-unit price snapshot; the persisted <see cref="UnitPrice"/> is the rounded line total used by
    /// payment allocation (VND has no minor unit in the payment flow).
    /// </summary>
    public static VisitCharge CreateMedicine(
        long visitId,
        long prescriptionId,
        long prescriptionItemId,
        long medicineId,
        decimal medicineUnitPrice,
        decimal quantity,
        DateTime createdAtLocal,
        bool waived = false)
    {
        if (visitId <= 0)
            throw new ArgumentOutOfRangeException(nameof(visitId));
        if (prescriptionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(prescriptionId));
        if (prescriptionItemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(prescriptionItemId));
        if (medicineId <= 0)
            throw new ArgumentOutOfRangeException(nameof(medicineId));
        if (medicineUnitPrice < 0m)
            throw new ArgumentOutOfRangeException(nameof(medicineUnitPrice));
        if (quantity <= 0m)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        var lineAmount = decimal.Round(medicineUnitPrice * quantity, 0, MidpointRounding.AwayFromZero);
        if (lineAmount > 999_999_999_999.99m)
            throw new ArgumentOutOfRangeException(nameof(medicineUnitPrice));

        return new VisitCharge
        {
            VisitId = visitId,
            ServiceId = null,
            DiagnosticOrderId = null,
            PrescriptionId = prescriptionId,
            PrescriptionItemId = prescriptionItemId,
            MedicineId = medicineId,
            Quantity = quantity,
            MedicineUnitPrice = medicineUnitPrice,
            ChargeType = VisitChargeTypes.Medicine,
            UnitPrice = lineAmount,
            PaymentRequirement = (waived || lineAmount == 0m) ? "WAIVED" : "REQUIRED",
            Status = VisitChargeStatuses.Active,
            CreatedAt = createdAtLocal,
        };
    }
}
