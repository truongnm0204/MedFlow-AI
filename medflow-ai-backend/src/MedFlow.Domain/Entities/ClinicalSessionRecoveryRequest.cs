using MedFlow.Domain.Constants;

namespace MedFlow.Domain.Entities;

/// <summary>
/// One clinical session recovery request (and, when granted, the single-use grant) for one clinical QueueEntry.
/// Bound to <c>(HospitalId, VisitId, QueueEntryId, AuthorizedDoctorId)</c> — never reusable elsewhere.
/// </summary>
public class ClinicalSessionRecoveryRequest
{
    public long Id { get; set; }
    public long HospitalId { get; set; }
    public long VisitId { get; set; }
    public long QueueEntryId { get; set; }

    /// <summary>Assigned doctor captured at request time; re-validated under lock, never trusted blindly.</summary>
    public long? OriginalDoctorId { get; set; }

    /// <summary>Delegated recipient doctor; null before a grant and null on the own-session branch.</summary>
    public long? AuthorizedDoctorId { get; set; }

    public long RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; }
    public string RequestReason { get; set; } = string.Empty;

    public long? GrantedByUserId { get; set; }
    public long? GrantedByDoctorId { get; set; }
    public DateTime? GrantedAt { get; set; }
    public string? GrantReason { get; set; }
    public DateTime? ExpiresAt { get; set; }

    /// <summary>QueueEntry.StateVersion the authority was issued against; re-checked under lock on confirm.</summary>
    public int ExpectedQueueStateVersion { get; set; }

    public string Status { get; set; } = ClinicalSessionRecoveryStatuses.Requested;

    /// <summary>True when the assigned doctor recovers their own interrupted session (no fabricated grant).</summary>
    public bool OwnSession { get; set; }

    public long? RecoveredByUserId { get; set; }
    public long? RecoveredByDoctorId { get; set; }
    public DateTime? RecoveredAt { get; set; }
    public string? RecoveryReason { get; set; }

    public long? RevokedByUserId { get; set; }
    public long? RevokedByDoctorId { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }

    public long? RejectedByUserId { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }

    public long? CancelledByUserId { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    public DateTime? ExpiredAt { get; set; }

    /// <summary>Confirmation idempotency: unique on (actor_user_id, operation, key). Never a bare client key.</summary>
    public string? IdempotencyKey { get; set; }
    public long? IdempotencyActorUserId { get; set; }
    public string? IdempotencyOperation { get; set; }

    /// <summary>SHA-256 fingerprint of the normalized confirm payload — replay with a different payload is rejected.</summary>
    public string? ConfirmationPayloadHash { get; set; }

    public int StateVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
