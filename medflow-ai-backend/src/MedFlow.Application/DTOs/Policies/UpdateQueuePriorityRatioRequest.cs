namespace MedFlow.Application.DTOs.Policies;

public sealed class UpdateQueuePriorityRatioRequest
{
    public int? Priority { get; set; }
    public int? Normal { get; set; }
}
