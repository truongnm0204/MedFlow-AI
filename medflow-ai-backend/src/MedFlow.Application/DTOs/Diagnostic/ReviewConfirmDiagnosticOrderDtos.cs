namespace MedFlow.Application.DTOs.Diagnostic;

public sealed record GetDiagnosticOrdersForReviewRequest(
    string? Status = null,
    string? Category = null,
    string? Priority = null,
    long? VisitId = null,
    string? VisitCode = null,
    long? ServiceId = null,
    long? RoomId = null,
    long? ServicePointId = null,
    bool PendingOnly = false,
    string? Keyword = null,
    int Limit = 50);

public sealed record ConfirmDiagnosticOrderRequest(
    bool? IsEligibilityVerified = null,
    bool? EligibilityConfirmed = null,
    long? ServicePointId = null,
    long? RoomId = null,
    string? Priority = null,
    string? Instruction = null,
    string? ConfirmationNote = null,
    string? ReviewNote = null,
    string? Notes = null,
    bool? PatientPresent = null,
    int? ExpectedStateVersion = null);

public sealed record ConfirmDiagnosticOrdersBatchRequest(
    long? OrderId = null,
    string? OrderCode = null,
    IReadOnlyList<long>? OrderIds = null,
    IReadOnlyList<string>? OrderCodes = null,
    bool? IsEligibilityVerified = null,
    bool? EligibilityConfirmed = null,
    long? ServicePointId = null,
    long? RoomId = null,
    string? Priority = null,
    string? Instruction = null,
    string? ConfirmationNote = null,
    string? ReviewNote = null,
    string? Notes = null,
    bool? PatientPresent = null,
    int? ExpectedStateVersion = null);

public sealed record DiagnosticOrderPatientSummaryDto(
    long PatientId,
    string PatientCode,
    string FullName,
    DateOnly? DateOfBirth,
    int? Age,
    string? Gender,
    string? Phone,
    string? BloodType,
    string? Allergies);

public sealed record DiagnosticOrderClinicalSummaryDto(
    long VisitId,
    string VisitCode,
    string VisitStatus,
    string? ReasonForVisit,
    string? PresentingComplaint,
    string? ClinicalFindings,
    string? PrimaryDiagnosisCode,
    string? PrimaryDiagnosisName,
    string? ClinicalDiagnosis);

public sealed record DiagnosticOrderEligibilityCheckDto(
    bool IsEligible,
    bool CanConfirm,
    string? DisabledReason,
    bool IsOrderPendingConfirmation,
    bool IsVisitEligible,
    bool IsPatientActive,
    bool IsServiceActive,
    bool IsDepartmentActive,
    bool IsDoctorAuthorizedForService,
    bool IsServicePointActive,
    bool IsRoomActive,
    bool IsQueueEntryEligible);

public sealed record DiagnosticServicePointOptionDto(
    long ServicePointId,
    string Name,
    long? ServiceId,
    long? RoomId,
    string? RoomCode,
    string? RoomName,
    string? RoomLocation,
    bool IsActive,
    bool IsRoomActive);

public sealed record DiagnosticOrderReviewItemDto(
    long OrderId,
    string OrderCode,
    long VisitId,
    string VisitCode,
    string VisitStatus,
    DiagnosticOrderPatientSummaryDto Patient,
    long? OrderingDoctorId,
    string? OrderingDoctorCode,
    string OrderingDoctorName,
    long ServiceId,
    string ServiceCode,
    string ServiceName,
    string Category,
    long? DepartmentId,
    string? DepartmentCode,
    string DepartmentName,
    long? SpecialtyId,
    string? SpecialtyCode,
    string SpecialtyName,
    decimal UnitPrice,
    bool RequiresResultVerification,
    string Priority,
    string Status,
    bool RequiredForConclusion,
    string? Instruction,
    DateTime OrderedAt,
    DateTime? ConfirmedAt,
    long? ConfirmedByStaffId,
    string? ConfirmedByStaffCode,
    string? ConfirmedByStaffName,
    string? ConfirmationNote,
    long? QueueEntryId,
    string? FormattedQueueEntryId,
    string? TicketNo,
    int? QueueNumber,
    DateOnly? QueueDate,
    string? QueueStatus,
    string? PresenceStatus,
    int StateVersion,
    long? ServicePointId,
    string? ServicePointName,
    long? RoomId,
    string? RoomCode,
    string? RoomName,
    string? RoomLocation,
    bool IsAdditionalOrder,
    int OrderSequenceNo,
    long? FollowUpToOrderId,
    string? FollowUpToOrderCode,
    string? AdditionalOrderReason,
    bool CanConfirm,
    string? DisabledReason,
    DiagnosticOrderEligibilityCheckDto Eligibility);

public sealed record DiagnosticOrderReviewSummaryStatsDto(
    int TotalOrders,
    int PendingConfirmationCount,
    int ConfirmedCount,
    int InProgressCount,
    int CompletedCount,
    int CancelledCount,
    int UrgentOrStatPendingCount);

public sealed record DiagnosticOrderReviewListResponseDto(
    long HospitalId,
    string HospitalCode,
    string HospitalName,
    long? DepartmentId,
    string DepartmentName,
    DateTime GeneratedAtUtc,
    DiagnosticStaffProfileDto StaffProfile,
    DiagnosticOrderReviewSummaryStatsDto Summary,
    IReadOnlyList<DiagnosticServicePointOptionDto> AvailableServicePoints,
    IReadOnlyList<DiagnosticOrderReviewItemDto> Items);

public sealed record DiagnosticOrderReviewDetailResponseDto(
    long HospitalId,
    string HospitalCode,
    string HospitalName,
    DateTime GeneratedAtUtc,
    DiagnosticStaffProfileDto StaffProfile,
    DiagnosticOrderReviewItemDto Order,
    DiagnosticOrderClinicalSummaryDto ClinicalContext,
    IReadOnlyList<DiagnosticServicePointOptionDto> AvailableServicePoints);

public sealed record ConfirmDiagnosticOrderResponseDto(
    string ActionOutcome,
    int ConfirmedCount,
    DateTime ConfirmedAt,
    DiagnosticStaffProfileDto ConfirmedByStaff,
    DiagnosticOrderReviewItemDto Order,
    IReadOnlyList<DiagnosticOrderReviewItemDto> ConfirmedOrders);
