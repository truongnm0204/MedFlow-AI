namespace MedFlow.Domain.Entities;

public class DiagnosticResultVersion
{
    public long Id { get; set; }
    public long? DiagnosticResultId { get; set; }
    public int? VersionNo { get; set; }
    public string? ResultText { get; set; }
    public string? Conclusion { get; set; }
    public string? ReportFileUrl { get; set; }
    public long? EnteredByStaffId { get; set; }
    public DateTime? EnteredAt { get; set; }
    public bool? IsFinalized { get; set; }
    public long? VerifiedByStaffId { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? CorrectionReason { get; set; }
}
