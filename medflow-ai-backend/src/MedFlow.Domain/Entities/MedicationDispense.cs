namespace MedFlow.Domain.Entities;

public class MedicationDispense
{
    public long Id { get; set; }
    public long? PrescriptionId { get; set; }
    public long? PreparedByStaffId { get; set; }
    public long? DispensedByStaffId { get; set; }
    public string? Status { get; set; }
    public DateTime? PreparedAt { get; set; }
    public DateTime? DispensedAt { get; set; }
    public string? IdempotencyKey { get; set; }
}
