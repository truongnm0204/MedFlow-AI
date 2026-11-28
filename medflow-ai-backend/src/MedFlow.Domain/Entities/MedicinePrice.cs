namespace MedFlow.Domain.Entities;

/// <summary>
/// Bảng giá thuốc có hiệu lực (owner decision D1: giá theo THUỐC, không theo lô).
/// Half-open interval [<see cref="EffectiveFrom"/>, <see cref="EffectiveTo"/>);
/// <see cref="EffectiveTo"/> = null nghĩa là còn hiệu lực.
/// </summary>
public class MedicinePrice
{
    public long Id { get; set; }
    public long HospitalId { get; set; }
    public long MedicineId { get; set; }
    public decimal PriceAmount { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? DecisionNumber { get; set; }
    public string? AppliedBy { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? CreatedByUserId { get; set; }

    public Medicine? Medicine { get; set; }
}
