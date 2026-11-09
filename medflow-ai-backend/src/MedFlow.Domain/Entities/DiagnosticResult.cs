namespace MedFlow.Domain.Entities;

public class DiagnosticResult
{
    public long Id { get; set; }
    public long? DiagnosticOrderId { get; set; }
    public string? Status { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public long? FinalizedByStaffId { get; set; }
}
