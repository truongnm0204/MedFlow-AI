namespace MedFlow.Application.Services.Visits.Queueing;

internal static class QueuePolicyKeys
{
    public const string PolicyType = "QUEUE";

    public const string GraceMinutes = "grace_minutes";
    public const string ExpectedDurationMinutes = "expected_duration_minutes";
    public const string SafetyBufferMinutes = "safety_buffer_minutes";

    public const string PropMinutes = "minutes";

    public const int DefaultGraceMinutes = 15;
}
