namespace MedFlow.Domain.Entities;

public class MedicalRecordRevision
{
    public long Id { get; set; }
    public long? MedicalRecordId { get; set; }
    public int? RevisionNo { get; set; }
    public string? PresentingComplaint { get; set; }
    public string? SymptomHistory { get; set; }
    public string? RelevantHistoryAllergies { get; set; }
    public string? VitalSigns { get; set; }
    public string? ClinicalFindings { get; set; }
    public string? ClinicalConclusion { get; set; }
    public long? EditedByDoctorId { get; set; }
    public DateTime? RecordedAt { get; set; }
    public bool? IsConfirmed { get; set; }
    public string? AmendmentReason { get; set; }
}
