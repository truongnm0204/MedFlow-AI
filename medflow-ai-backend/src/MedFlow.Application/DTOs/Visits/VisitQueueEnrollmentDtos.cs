namespace MedFlow.Application.DTOs.Visits;

public static class QueueEnrollmentTargets
{
    public const string Diagnostic = "DIAGNOSTIC";
    public const string ReturnConsultation = "RETURN_CONSULTATION";
    public const string Pharmacy = "PHARMACY";
}

public sealed record VisitQueueEnrollmentTicketDto(
    long QueueEntryId,
    string FormattedQueueEntryId,
    string TicketNo,
    int QueueNumber,
    string QueueDate,
    string QueueType,
    string Status,
    string PresenceStatus,
    string Priority,
    long? ServicePointId,
    string? ServicePointName,
    long? RoomId,
    string? RoomCode,
    string? RoomName,
    long? DiagnosticOrderId,
    long? PrescriptionId,
    string? CoordinationPurpose,
    int StateVersion,
    bool IsIdempotentReplay = false);

public sealed record VisitQueueEnrollmentDiagnosticItemDto(
    long OrderId,
    string OrderCode,
    long ServiceId,
    string ServiceCode,
    string ServiceName,
    long? DepartmentId,
    string? DepartmentName,
    string SelectionStatus,
    string OrderStatus,
    string SettlementStatus,
    bool IsPaymentSettled,
    bool IsExpiredForExecution,
    bool CanEnrollNow,
    string? BlockedReasonCode,
    string? BlockedReasonMessage,
    long? CompatibleServicePointId,
    string? CompatibleServicePointName,
    long? CompatibleRoomId,
    string? CompatibleRoomCode,
    string? CompatibleRoomName,
    long? CurrentQueueEntryId,
    string? CurrentQueueStatus);

public sealed record VisitQueueEnrollmentReturnSummaryDto(
    bool IsReadyForReturn,
    DateTime? ReadyForReturnAt,
    bool HasReviewRequiredOrExpiredOrders,
    bool RequiresReceptionCoordination,
    string ReceptionGuidanceMessage,
    VisitQueueEnrollmentTicketDto? CurrentReturnTicket);

public sealed record VisitQueueEnrollmentPharmacyItemDto(
    long PrescriptionId,
    string PrescriptionCode,
    string PrescriptionStatus,
    string? FulfillmentChoice,
    bool IsPaymentCleared,
    bool CanEnrollNow,
    string? BlockedReasonCode,
    string? BlockedReasonMessage,
    long? CurrentQueueEntryId,
    string? CurrentQueueStatus);

public sealed record VisitQueueEnrollmentContextDto(
    long VisitId,
    string VisitCode,
    string VisitStatus,
    string PresenceState,
    bool IsPresentToday,
    int PresenceSessionNo,
    bool ClinicalHandoffCompleted,
    VisitQueueEnrollmentTicketDto? ActiveQueueEntry,
    IReadOnlyList<VisitQueueEnrollmentDiagnosticItemDto> DiagnosticItems,
    VisitQueueEnrollmentReturnSummaryDto ReturnConsultationSummary,
    IReadOnlyList<VisitQueueEnrollmentPharmacyItemDto> PharmacyItems);

/// <param name="ServicePointId">
/// D2 / BR-BF02-06: bị BỎ QUA với <c>DIAGNOSTIC</c> — hệ thống tự resolver điểm CLS. Vẫn dùng cho
/// <c>PHARMACY</c> / <c>RETURN_CONSULTATION</c>.
/// </param>
public sealed record CreateVisitQueueEnrollmentRequestDto(
    string Target,
    long? DiagnosticOrderId = null,
    long? PrescriptionId = null,
    long? ServicePointId = null,
    string? IdempotencyKey = null,
    long? ReplacementDoctorId = null,
    string? ReassignmentReason = null,
    string? Purpose = null);

public sealed record LeaveQueueEntryRequestDto(
    string? Reason = null,
    bool PatientLeftHospital = false,
    int? ExpectedStateVersion = null);

public sealed record LeaveQueueEntryResponseDto(
    long QueueEntryId,
    string FormattedQueueEntryId,
    string TicketNo,
    string QueueType,
    string PreviousStatus,
    string Status,
    string PresenceStatus,
    long? VisitId,
    string? VisitPresenceState,
    int? VisitPresenceSessionNo,
    int StateVersion,
    DateTime LeftAtUtc);

public sealed record PharmacyQueueActionRequestDto(
    string? TargetStatus = null,
    string? Reason = null,
    int? ExpectedStateVersion = null);
