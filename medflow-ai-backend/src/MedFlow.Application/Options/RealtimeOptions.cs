namespace MedFlow.Application.Options;

public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    public int MaxGroupsPerConnection { get; set; } = 20;

    public int MaxInvocationsPerMinute { get; set; } = 30;

    public long MaxReceiveMessageSizeBytes { get; set; } = 8 * 1024;

    public int MembershipTtlMinutes { get; set; } = 5;

    public int PublishTimeoutSeconds { get; set; } = 3;
}
