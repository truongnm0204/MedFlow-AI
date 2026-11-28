namespace MedFlow.Application.Interfaces.Services;

public sealed record VisitChargeGateResult(
    bool IsUnlocked,
    string StatusCode,
    long? VisitChargeId,
    decimal? UnitPrice,
    string? Message = null);

/// <summary>
/// Shared Per-Charge Pay-Before-Service eligibility reader (ADR-0059).
/// Implemented in Phase 03 and consumed by Reception/Queue (Phase 05), Consultation/Diagnostic (Phase 06), and Pharmacy (Phase 03).
/// </summary>
public interface IVisitChargeSettlementReader
{
    Task<VisitChargeGateResult> CheckExaminationEligibilityAsync(
        long visitId,
        CancellationToken ct = default);

    Task<VisitChargeGateResult> CheckDiagnosticOrderEligibilityAsync(
        long diagnosticOrderId,
        CancellationToken ct = default);

    Task<VisitChargeGateResult> CheckPharmacyVisitClearanceAsync(
        long visitId,
        CancellationToken ct = default);

    /// <summary>
    /// #866: medicine charges of one prescription must be settled at the pharmacy counter before hand-over.
    /// Returns unlocked when the prescription has no priced MEDICINE charge (no billable medicine to collect).
    /// </summary>
    Task<VisitChargeGateResult> CheckPharmacyMedicineEligibilityAsync(
        long visitId,
        long prescriptionId,
        CancellationToken ct = default);
}
