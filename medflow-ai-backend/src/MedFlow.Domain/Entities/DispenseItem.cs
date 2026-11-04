namespace MedFlow.Domain.Entities;

public class DispenseItem
{
    public long Id { get; set; }
    public long? DispenseId { get; set; }
    public long? PrescriptionItemId { get; set; }
    public long? MedicineBatchId { get; set; }
    public decimal? PlannedQuantity { get; set; }
    public decimal? DispensedQuantity { get; set; }
    public string? ShortageReason { get; set; }
}
