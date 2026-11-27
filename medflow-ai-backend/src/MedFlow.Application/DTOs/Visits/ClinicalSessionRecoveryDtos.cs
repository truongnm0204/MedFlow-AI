namespace MedFlow.Application.DTOs.Visits;

/// <summary>Payload for POST /api/v1/visits/{idOrCode}/clinical-session-recovery-requests.</summary>
public sealed record CreateClinicalSessionRecoveryRequestDto(
    long QueueEntryId,
    int ExpectedStateVersion,
    string Reason);

/// <summary>Payload for POST /api/v1/clinical-session-recovery-requests/{id}/grant.</summary>
public sealed record GrantClinicalSessionRecoveryRequestDto(
    long AuthorizedDoctorId,
    int ExpectedStateVersion,
    string Reason);

/// <summary>Payload for reject / revoke / cancel.</summary>
public sealed record ClinicalSessionRecoveryDecisionRequestDto(
    string Reason,
    int? ExpectedStateVersion = null);

/// <summary>Payload for POST /api/v1/clinical-session-recovery-requests/{id}/confirm.</summary>
public sealed record ConfirmClinicalSessionRecoveryRequestDto(
    int ExpectedStateVersion,
    bool ConfirmInterrupted,
    string Reason,
    string? IdempotencyKey = null);

public sealed record ClinicalSessionRecoveryQueueDto(
    long QueueEntryId,
    string QueueType,
    string Status,
    int StateVersion,
    DateOnly? QueueDate,
    DateTime? CompletedAt);

/// <summary>
/// Deliberately excludes reasons / clinical content: only identity, lifecycle and state-version metadata.
/// Reasons live in audit_logs only (never in realtime payloads or log lines).
/// </summary>
public sealed record ClinicalSessionRecoveryRequestDto(
    long RequestId,
    string Status,
    long HospitalId,
    long VisitId,
    long QueueEntryId,
    long? OriginalDoctorId,
    long? AuthorizedDoctorId,
    bool OwnSession,
    int ExpectedQueueStateVersion,
    long RequestedByUserId,
    DateTime RequestedAt,
    DateTime? GrantedAt,
    DateTime? ExpiresAt,
    DateTime? RecoveredAt,
    DateTime? RevokedAt,
    DateTime? RejectedAt,
    DateTime? CancelledAt,
    DateTime? ExpiredAt,
    string? RequestReason = null,
    string? GrantReason = null,
    string? RecoveryReason = null,
    string? RevocationReason = null,
    string? RejectionReason = null,
    string? CancellationReason = null);

public sealed record ClinicalSessionRecoveryCapabilitiesDto(
    bool CanRequest,
    bool CanGrant,
    bool CanConfirmOwnSession,
    bool CanConfirmDelegated,
    bool CanReject,
    bool CanRevoke,
    bool CanCancel);

public sealed record ClinicalSessionRecoveryContextDto(
    long VisitId,
    string VisitCode,
    string VisitStatus,
    long? HospitalId,
    long? AssignedDoctorId,
    string? AssignedDoctorCode,
    string? AssignedDoctorName,
    IReadOnlyList<ClinicalSessionRecoveryQueueDto> Queues,
    IReadOnlyList<ClinicalSessionRecoveryRequestDto> Requests,
    ClinicalSessionRecoveryCapabilitiesDto Capabilities);

public sealed record ClinicalSessionRecoveryConfirmResponseDto(
    long RequestId,
    string Status,
    long VisitId,
    string VisitCode,
    long QueueEntryId,
    string QueueStatus,
    int QueueStateVersion,
    string VisitStatus,
    DateTime RecoveredAt,
    bool IsIdempotentReplay,
    string? RecoveryReason = null);

/// <summary>
/// Scoped list projection for GET /api/v1/clinical-session-recovery-requests.
/// Reasons are included for actors that passed visibility/scope (D7 revise); never in errors/SignalR.
/// </summary>
public sealed record ClinicalSessionRecoveryListItemDto(
    long RequestId,
    string Status,
    long HospitalId,
    long VisitId,
    string VisitCode,
    long QueueEntryId,
    long? OriginalDoctorId,
    long? AuthorizedDoctorId,
    int ExpectedQueueStateVersion,
    DateTime RequestedAt,
    DateTime? GrantedAt,
    DateTime? ExpiresAt,
    DateTime? RecoveredAt,
    string? RequestReason = null,
    string? GrantReason = null,
    string? RecoveryReason = null,
    string? RevocationReason = null,
    string? RejectionReason = null,
    string? CancellationReason = null);

/// <summary>Candidate doctor for POST grant — minimal identity/department/specialty for selection.</summary>
public sealed record ClinicalSessionRecoveryEligibleRecipientDto(
    long DoctorId,
    string DoctorCode,
    string DoctorName,
    long? DepartmentId,
    string? DepartmentName,
    long? SpecialtyId,
    string? SpecialtyName);

