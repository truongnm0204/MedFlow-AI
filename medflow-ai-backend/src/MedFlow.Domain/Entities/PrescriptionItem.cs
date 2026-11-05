namespace MedFlow.Domain.Entities;

public class PrescriptionItem
{
    public long Id { get; set; }
    public long? PrescriptionId { get; set; }
    public long? MedicineId { get; set; }
    public decimal? PrescribedQuantity { get; set; }
    public decimal? SingleDoseQuantity { get; set; }
    public string? SingleDoseUnit { get; set; }
    public string? DoseScheduleText { get; set; }
    public string? Frequency { get; set; }
    public int? DurationDays { get; set; }
    public string? AdministrationRoute { get; set; }
    public string? Instructions { get; set; }
}
