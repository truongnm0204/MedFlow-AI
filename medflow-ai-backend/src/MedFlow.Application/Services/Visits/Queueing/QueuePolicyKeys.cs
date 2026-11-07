namespace MedFlow.Application.Services.Visits.Queueing;

internal static class QueuePolicyKeys
{
    public const string PolicyType = "QUEUE";
    public const string DiagnosticPolicyType = "DIAGNOSTIC";

    public const string GraceMinutes = "grace_minutes";
    public const string ExpectedDurationMinutes = "expected_duration_minutes";
    public const string SafetyBufferMinutes = "safety_buffer_minutes";
    public const string FinalResultRequired = "final_result_required";
    public const string WalkinPriorityRatio = "walkin_priority_ratio";

    public const string PropMinutes = "minutes";
    public const string PropEnabled = "enabled";
    public const string PropPriority = "priority";
    public const string PropNormal = "normal";

    public const int DefaultGraceMinutes = 15;
    public const int DefaultPriorityWeight = 3;
    public const int DefaultNormalWeight = 1;
    public const int MinRatioWeight = 1;
    public const int MaxRatioWeight = 10;
}
