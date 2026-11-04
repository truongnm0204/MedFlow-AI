namespace MedFlow.Domain.Entities;

public class QueueEntry
{
    public long Id { get; set; }
    public long? VisitId { get; set; }
    public long? AppointmentId { get; set; }
    public long? ServicePointId { get; set; }
    public long? CurrentServicePointId { get; set; }
    public DateOnly? QueueDate { get; set; }
    public int? QueueNumber { get; set; }
    public string? QueueType { get; set; }
    public string? Status { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? PresenceStatus { get; set; }
    public DateTime? ArrivedAt { get; set; }
    public string? ArrivalClass { get; set; }
    public string? ManualPriority { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? CalledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? BlockedReason { get; set; }
}
