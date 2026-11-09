namespace MedFlow.Domain.Entities;

public class ClinicalAddendum
{
    public long Id { get; set; }
    public long? VisitId { get; set; }
    public string? RecordType { get; set; }
    public long? TargetRecordId { get; set; }
    public string? CorrectedContent { get; set; }
    public string? Reason { get; set; }
    public long? AuthorDoctorId { get; set; }
    public DateTime? RecordedAt { get; set; }
}
