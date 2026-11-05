namespace MedFlow.Domain.Entities;

public class MedicineBatch
{
    public long Id { get; set; }
    public long? MedicineId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal? QuantityOnHand { get; set; }
    public bool? IsBlocked { get; set; }
}
