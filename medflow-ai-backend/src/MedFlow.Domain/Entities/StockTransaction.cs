namespace MedFlow.Domain.Entities;

public class StockTransaction
{
    public long Id { get; set; }
    public long? MedicineBatchId { get; set; }
    public long? DispenseItemId { get; set; }
    public string? TransactionType { get; set; }
    public decimal? QuantityDelta { get; set; }
    public long? PerformedByStaffId { get; set; }
    public string? Reason { get; set; }
    public string? SourceReference { get; set; }
    public DateTime? OccurredAt { get; set; }
}
