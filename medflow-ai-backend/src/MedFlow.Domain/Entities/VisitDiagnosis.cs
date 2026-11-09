namespace MedFlow.Domain.Entities;

public class VisitDiagnosis
{
    public long Id { get; set; }
    public long? VisitId { get; set; }
    public long? Icd10CodeId { get; set; }
    public bool? IsPrimary { get; set; }
    public string? CodeSnapshot { get; set; }
    public string? NameSnapshot { get; set; }
    public string? CatalogVersion { get; set; }
    public string? ClinicalDiagnosis { get; set; }
    public string? Status { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public long? RecordedByDoctorId { get; set; }
    public DateTime? RecordedAt { get; set; }
}
