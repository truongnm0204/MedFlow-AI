namespace MedFlow.Application.DTOs.Consultation;

public sealed record CreateDiagnosticOrderRequest(
    long? ServiceId = null,
    string? ServiceCode = null,
    string? Instruction = null,
    string? Priority = null,
    bool? RequiredForConclusion = null,
    IReadOnlyList<CreateDiagnosticOrderItemRequest>? Items = null,
    int? ExpectedStateVersion = null,
    bool? TransitionToAwaitingResults = null,
    string? ClinicalNote = null,
    bool? IsAdditionalOrder = null,
    long? FollowUpToOrderId = null,
    string? FollowUpToOrderCode = null,
    string? AdditionalOrderReason = null,
    string? ClinicalProgressionNote = null,
    int? ExpectedRevisionNo = null,
    bool? PatientAccepted = null);

public sealed record CreateDiagnosticOrderItemRequest(
    long? ServiceId = null,
    string? ServiceCode = null,
    string? Instruction = null,
    string? Priority = null,
    bool? RequiredForConclusion = null,
    long? FollowUpToOrderId = null,
    string? FollowUpToOrderCode = null,
    string? AdditionalOrderReason = null,
    bool? PatientAccepted = null);

public sealed record CreateAdditionalDiagnosticOrderRequest(
    long? ServiceId = null,
    string? ServiceCode = null,
    string? Instruction = null,
    string? Priority = null,
    bool? RequiredForConclusion = null,
    IReadOnlyList<CreateDiagnosticOrderItemRequest>? Items = null,
    int? ExpectedStateVersion = null,
    int? ExpectedRevisionNo = null,
    bool? TransitionToAwaitingResults = null,
    string? ClinicalNote = null,
    string? AdditionalOrderReason = null,
    string? ClinicalProgressionNote = null,
    long? FollowUpToOrderId = null,
    string? FollowUpToOrderCode = null,
    bool? PatientAccepted = null);

public sealed record ConsultationDiagnosticOrderDto(
    long OrderId,
    string OrderCode,
    long VisitId,
    string VisitCode,
    long OrderingDoctorId,
    string OrderingDoctorCode,
    string OrderingDoctorName,
    long ServiceId,
    string ServiceCode,
    string ServiceName,
    string Category,
    decimal UnitPrice,
    bool RequiresResultVerification,
    long? QueueEntryId,
    string? FormattedQueueEntryId,
    string? TicketNo,
    int? QueueNumber,
    string? QueueStatus,
    long? ServicePointId,
    string? ServicePointName,
    long? RoomId,
    string? RoomCode,
    string? RoomName,
    string Priority,
    string Status,
    bool RequiredForConclusion,
    string? Instruction,
    DateTime OrderedAt,
    DateTime? ConfirmedAt,
    bool IsResultFinalized,
    long? ResultId = null,
    string? ResultStatus = null,
    DateTime? ResultFinalizedAt = null,
    int? ResultVersionNo = null,
    string? ResultText = null,
    string? Conclusion = null,
    string? ReportFileUrl = null,
    string? PerformedByStaffName = null,
    string? VerifiedByStaffName = null,
    bool IsAdditionalOrder = false,
    int OrderSequenceNo = 1,
    long? FollowUpToOrderId = null,
    string? FollowUpToOrderCode = null,
    string? AdditionalOrderReason = null,
    string SelectionStatus = "ACCEPTED",
    long? VisitChargeId = null,
    string? ChargeStatus = null,
    string? PaymentStatus = null,
    DateOnly? DeferredUntilDate = null,
    bool IsExpiredForReview = false,
    DateTime? ValidityExpiresAt = null,
    DateTime? ValidityExtendedUntil = null,
    DateTime? RevalidatedAt = null,
    string? RevalidationNote = null);

public sealed record DiagnosticServiceCatalogItemDto(
    long ServiceId,
    string ServiceCode,
    string Name,
    string? Description,
    string Category,
    long? DepartmentId,
    string? DepartmentCode,
    string DepartmentName,
    long? SpecialtyId,
    string? SpecialtyCode,
    string SpecialtyName,
    decimal UnitPrice,
    bool RequiresResultVerification,
    bool IsDoctorAuthorized,
    long? ServicePointId,
    string? ServicePointName,
    long? RoomId,
    string? RoomCode,
    string? RoomName,
    bool HasActiveOrderInVisit,
    bool HasCompletedOrderInVisit = false,
    int PreviousOrderCountInVisit = 0,
    string? LatestOrderCodeInVisit = null,
    string? LatestOrderStatusInVisit = null);

public sealed record DiagnosticOrderCatalogResponseDto(
    long VisitId,
    string VisitCode,
    string VisitStatus,
    string WorkspaceState,
    bool CanCreateDiagnosticOrder,
    string? DisabledReason,
    DateTime GeneratedAtUtc,
    ConsultationDiagnosticSummaryDto DiagnosticSummary,
    IReadOnlyList<DiagnosticServiceCatalogItemDto> Services,
    IReadOnlyList<ConsultationDiagnosticOrderDto> ExistingOrders,
    bool CanCreateAdditionalDiagnosticOrder = false,
    int CompletedOrdersCount = 0);

public sealed record CancelConsultationDiagnosticOrderRequest(
    string? Reason = null,
    bool? TechnicalAbort = null,
    string? CancelDisposition = null,
    int? ExpectedStateVersion = null,
    bool? VoluntarySkipByPatient = null,
    bool? ReconciliationRequired = null);

public sealed record RevalidateDiagnosticOrderRequest(
    string Reason,
    int? ExtendDays = null,
    DateTime? ValidityExtendedUntil = null,
    bool? TransitionToAwaitingResults = null,
    int? ExpectedStateVersion = null);

public sealed record ApproveOverdueReturnResultsRequest(
    string Reason,
    int? ExpectedStateVersion = null,
    string? ExpectedResultSnapshot = null);

public sealed record CompleteClinicalReviewRequest(
    int? ExpectedStateVersion = null,
    string? ClinicalNote = null);

public sealed record HandoffToDiagnosticRequest(
    int? ExpectedStateVersion = null,
    string? ClinicalNote = null);
