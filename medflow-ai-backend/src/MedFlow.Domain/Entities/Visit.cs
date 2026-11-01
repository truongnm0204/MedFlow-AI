namespace MedFlow.Domain.Entities;

public class Visit
{
    public long Id { get; set; }
    public long? AppointmentId { get; set; }
    public string? ReasonForVisit { get; set; }
    public string? Status { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
