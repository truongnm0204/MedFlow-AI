namespace MedFlow.Application.Services.Pharmacy;

/// <summary>
/// Snapshot of the medicine price effective at a given hospital-local instant.
/// Half-open interval: <see cref="EffectiveFrom"/> &lt;= t &lt; <see cref="EffectiveTo"/> (null = open).
/// </summary>
public sealed record MedicinePriceQuote(
    long MedicinePriceId,
    long HospitalId,
    long MedicineId,
    decimal PriceAmount,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo);

/// <summary>
/// Contract for downstream billing (#866): resolve the medicine price effective at a time.
/// Read-only and hospital-scoped so a caller can never borrow another hospital's price.
/// </summary>
public interface IMedicinePriceResolver
{
    /// <summary>
    /// Returns the price effective at <paramref name="atHospitalLocal"/> (Asia/Ho_Chi_Minh wall clock,
    /// <see cref="DateTimeKind.Unspecified"/>), or null when the medicine has no price covering that instant.
    /// </summary>
    Task<MedicinePriceQuote?> TryGetEffectivePriceAsync(
        long hospitalId,
        long medicineId,
        DateTime atHospitalLocal,
        CancellationToken ct = default);

    /// <summary>Same as above, evaluated at the current hospital clock.</summary>
    Task<MedicinePriceQuote?> TryGetEffectivePriceAsync(
        long hospitalId,
        long medicineId,
        CancellationToken ct = default);
}
