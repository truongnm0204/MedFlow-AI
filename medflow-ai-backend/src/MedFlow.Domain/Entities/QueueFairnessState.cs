namespace MedFlow.Domain.Entities;

public class QueueFairnessState
{
    public long ServicePointId { get; set; }
    public DateOnly QueueDate { get; set; }
    public int? ConsecutivePriorityStarted { get; set; }
    public long? LastStartedQueueEntryId { get; set; }
    public int? PolicyVersion { get; set; }
}
