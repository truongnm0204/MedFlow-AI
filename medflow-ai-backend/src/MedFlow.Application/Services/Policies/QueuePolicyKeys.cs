namespace MedFlow.Application.Services.Policies;

internal static class QueuePolicyKeys
{
    public const string PolicyType = "QUEUE";

    public const string GraceMinutes = "grace_minutes";
    public const string ExpectedDurationMinutes = "expected_duration_minutes";
    public const string SafetyBufferMinutes = "safety_buffer_minutes";
    public const string WalkinPriorityRatio = "walkin_priority_ratio";
    public const string PublicNumberImmutable = "public_number_immutable";
    public const string ManualPriorityRequiresVerification = "manual_priority_requires_verification";

    public const string PropMinutes = "minutes";
    public const string PropPriority = "priority";
    public const string PropNormal = "normal";
    public const string PropEnabled = "enabled";

    public const int DefaultGraceMinutes = 15;
    public const int DefaultExpectedDurationMinutes = 20;
    public const int DefaultSafetyBufferMinutes = 5;
    public const int DefaultPriorityWeight = 3;
    public const int DefaultNormalWeight = 1;
    public const bool DefaultPublicNumberImmutable = true;
    public const bool DefaultManualPriorityRequiresVerification = true;
}
