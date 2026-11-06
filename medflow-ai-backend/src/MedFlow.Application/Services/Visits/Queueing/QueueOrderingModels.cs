namespace MedFlow.Application.Services.Visits.Queueing;

public sealed record QueueOrderingPolicy(
    int GraceMinutes,
    int? ExpectedDurationMinutes,
    int? SafetyBufferMinutes);

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
    int QueueNumber);

public sealed record QueueEntryEvaluation(
    int ServingOrder,
    string EffectiveArrivalClass,
    bool IsEligible,
    bool IsAppointmentProtected,
    bool IsDueProtected,
    bool CanSafeBackfill,
    string? HoldReason,
    bool HasDueProtectedAppointment,
    DateTime? ProtectedNextStartsAt);

public sealed record QueueOrderingResult(
    IReadOnlyList<int> OrderedIndices,
    IReadOnlyList<QueueEntryEvaluation> Evaluations);
