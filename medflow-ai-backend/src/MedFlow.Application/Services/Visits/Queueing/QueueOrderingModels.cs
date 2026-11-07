namespace MedFlow.Application.Services.Visits.Queueing;

public sealed record QueueOrderingPolicy(
    int GraceMinutes,
    int? ExpectedDurationMinutes,
    int? SafetyBufferMinutes,
    bool FinalResultRequired = true);

public sealed record QueueOrderingItem(
    string Status,
    string Priority,
    string PresenceStatus,
    bool IsScheduled,
    string StoredArrivalClass,
    DateTime? ScheduledAt,
    DateTime? ArrivedAt,
    DateTime? ReadySince,
    DateTime? BackfilledAt,
    int QueueNumber,
    string QueueType = ClinicalQueueRules.QueueTypeExamination,
    string? BlockedReason = null,
    bool PendingRequiredResults = false);

public sealed record QueueEntryEvaluation(
    int ServingOrder,
    string EffectiveArrivalClass,
    bool IsEligible,
    bool IsAppointmentProtected,
    bool IsDueProtected,
    bool CanSafeBackfill,
    string? HoldReason,
    bool HasDueProtectedAppointment,
    DateTime? ProtectedNextStartsAt,
    string? IneligibleReason);

public static class QueueIneligibleReasons
{
    public const string NotArrived = "NOT_ARRIVED";
    public const string Left = "LEFT";
    public const string Blocked = "BLOCKED";
    public const string AwaitingResults = "AWAITING_RESULTS";
}

public static class QueueGroups
{
    public const string InProgress = "IN_PROGRESS";
    public const string Called = "CALLED";
    public const string Eligible = "ELIGIBLE";
    public const string NotEligible = "NOT_ELIGIBLE";
    public const string Skipped = "SKIPPED";
    public const string Completed = "COMPLETED";
}

public sealed record QueueOrderingResult(
    IReadOnlyList<int> OrderedIndices,
    IReadOnlyList<QueueEntryEvaluation> Evaluations);
