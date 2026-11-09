namespace MedFlow.Domain.Entities;

public class MedicalRecord
{
    public long Id { get; set; }
    public long? VisitId { get; set; }
    public long? CreatedByDoctorId { get; set; }
    public string? Status { get; set; }
    public DateTime? FinalizedAt { get; set; }
}
