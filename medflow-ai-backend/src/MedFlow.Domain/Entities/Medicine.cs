namespace MedFlow.Domain.Entities;

public class Medicine
{
    public long Id { get; set; }
    public long? HospitalId { get; set; }
    public string? MedicineCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ActiveIngredient { get; set; }
    public string? Strength { get; set; }
    public string? DosageForm { get; set; }
    public string? DispensingUnit { get; set; }
    public decimal? ContentQuantity { get; set; }
    public string? ContentUnit { get; set; }
    public bool? IsActive { get; set; }
}
