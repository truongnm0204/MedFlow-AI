namespace MedFlow.Application.DTOs.Visits;

public sealed record VisitChargesBriefDto(
    decimal PayableTotal,
    decimal RecognizedPaidTotal,
    decimal? AmountDue);

public sealed record VisitResumeDiagnosticOrderItemDto(
    long OrderId,
    string OrderCode,
    string ServiceName,
    bool RequiredForConclusion,
    string SelectionStatus,
    string OrderStatus,
    bool IsPaymentSettled,
    DateOnly? DeferredUntilDate,
    DateTime? ValidityExpiresAt,
    bool IsExpiredForReview,
    bool CanResumeToday,
    DateTime? ValidityExtendedUntil = null,
    string? RevalidationNote = null);

public sealed record VisitActiveQueueBriefDto(
    long QueueEntryId,
    string TicketNo,
    int QueueNumber,
    DateOnly QueueDate,
    string QueueType,
    string Status,
    string PresenceStatus,
    long? ServicePointId,
    string? ServicePointName,
    string? RoomCode,
    string? CoordinationPurpose = null);

public sealed record VisitResumeContextResponseDto(
    long VisitId,
    string VisitCode,
    DateTime? CheckedInAt,
    string VisitStatus,
    string PresenceState,
    DateOnly? LastPresentDate,
    DateTime? LastResumedAt,
    bool IsPresentToday,
    DateTime? ReadyForReturnAt,
    bool IsReadyForReturn,
    long? AssignedDoctorId,
    string? AssignedDoctorName,
    long? ExaminationRoomId,
    bool IsOverdueForReview,
    IReadOnlyList<string> OverdueReviewReasons,
    VisitChargesBriefDto ChargesSummary,
    IReadOnlyList<VisitResumeDiagnosticOrderItemDto> DiagnosticOrders,
    IReadOnlyList<VisitActiveQueueBriefDto> ActiveTodayQueues,
    bool IsAssignedDoctorActive = true,
    bool IsOverdueReturnApproved = false);

public sealed record ResumeCrossDayVisitRequestDto(
    string Target,
    long? DiagnosticOrderId = null,
    IReadOnlyList<long>? DiagnosticOrderIds = null,
    long? TargetServicePointId = null,
    long? ReassignDoctorId = null,
    bool? ConfirmOverdueOverride = null,
    string? IdempotencyKey = null,
    string? Notes = null,
    string? CoordinationPurpose = null,
    bool? ConfirmPresent = null);

public sealed record ResumeCrossDayVisitResponseDto(
    long VisitId,
    string VisitCode,
    DateTime? CheckedInAt,
    string VisitStatus,
    string PresenceState,
    DateOnly LastPresentDate,
    DateTime LastResumedAt,
    string Target,
    bool IsIdempotentReplay,
    long? AssignedDoctorId,
    string? AssignedDoctorName,
    long? ExaminationRoomId,
    IReadOnlyList<VisitActiveQueueBriefDto> ResumedQueues,
    int ClosedPriorDayQueueCount,
    string? CoordinationPurpose = null);

public sealed record DeferVisitOrOrdersRequestDto(
    string Reason,
    DateOnly? DeferredUntilDate = null,
    IReadOnlyList<long>? DiagnosticOrderIds = null,
    bool PatientLeftHospital = true);

public sealed record DeferVisitOrOrdersResponseDto(
    long VisitId,
    string VisitCode,
    string VisitStatus,
    string PresenceState,
    DateOnly? LastPresentDate,
    DateTime? ReadyForReturnAt,
    DateOnly? DeferredUntilDate,
    IReadOnlyList<long> DeferredQueueEntryIds,
    IReadOnlyList<long> UpdatedDiagnosticOrderIds);
