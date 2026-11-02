namespace MedFlow.Domain.Entities;

public class QueueEntry
{
    public long Id { get; set; }
    public long? AppointmentId { get; set; }
    public int? QueueNumber { get; set; }
    public string? QueueType { get; set; }
    public DateTime? CalledAt { get; set; }
    public DateTime? StartedAt { get; set; }
}
