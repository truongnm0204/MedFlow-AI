namespace MedFlow.Application.DTOs.Policies;

public sealed class UpdateQueuePolicyRequest
{
    public int? GraceMinutes { get; set; }
    public int? ExpectedDurationMinutes { get; set; }
    public int? SafetyBufferMinutes { get; set; }
    public UpdateQueuePriorityRatioRequest? WalkinPriorityRatio { get; set; }
    public bool? PublicNumberImmutable { get; set; }
    public bool? ManualPriorityRequiresVerification { get; set; }
}
