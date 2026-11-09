namespace MedFlow.Domain.Entities;

public class DiagnosticOrder
{
    public long Id { get; set; }
    public string? OrderCode { get; set; }
    public long? VisitId { get; set; }
    public long? OrderingDoctorId { get; set; }
    public long? ServiceId { get; set; }
    public long? QueueEntryId { get; set; }
    public string? Instruction { get; set; }
    public bool? RequiredForConclusion { get; set; }
    public string? Priority { get; set; }
    public string? Status { get; set; }
    public long? ConfirmedByStaffId { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? OrderedAt { get; set; }
}
