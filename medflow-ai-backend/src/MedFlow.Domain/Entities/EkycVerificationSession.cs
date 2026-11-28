using MedFlow.Domain.Constants;

namespace MedFlow.Domain.Entities;

/// <summary>
/// One eKYC verification session — evidence record for the single VNPT eKYC engine, whether
/// driven self-service or by reception staff. The session's <see cref="Purpose"/> decides the
/// identity artifact produced: account activation or a patient-profile identity verification.
/// Maps to <c>ekyc_verification_sessions</c> (migration 029).
/// NEVER stores raw document/face images: only OCR fields, verdicts, digests and the
/// AES-256-GCM encrypted VNPT payload snapshot.
/// </summary>
public class EkycVerificationSession
{
    public long Id { get; set; }

    /// <summary>Account being verified; null for patient-profile-only sessions (no account yet).</summary>
    public long? UserId { get; set; }

    /// <summary>
    /// Registration-scope session link. No FK — the registration-scope session mechanism
    /// (Phase 1 item 3) does not exist yet; reserved here to avoid a second schema touch.
    /// </summary>
    public long? RegistrationSessionId { get; set; }

    /// <summary>Linked patient profile (target for PATIENT_PROFILE purpose; resolved by dedupe for activation).</summary>
    public long? PatientId { get; set; }

    public string Status { get; set; } = EkycVerificationStatuses.Initiated;

    /// <summary>Who operated the session (single engine — never a verdict source).</summary>
    public string VerificationMode { get; set; } = EkycVerificationModes.SelfService;

    /// <summary>Identity artifact this session produces.</summary>
    public string Purpose { get; set; } = EkycPurposes.AccountActivation;

    /// <summary>OCR fields only (name/id/dob/... + prob scores). NEVER images.</summary>
    public string? OcrSnapshot { get; set; }

    /// <summary>Card front/back/face/masked liveness verdicts + messages.</summary>
    public string? LivenessVerdicts { get; set; }

    /// <summary>Face-vs-document compare result + prob (optional step; null when not run).</summary>
    public string? CompareResult { get; set; }

    /// <summary>Document tampering verdict (is_legal + warnings).</summary>
    public string? TamperingVerdict { get; set; }

    /// <summary>Original VNPT dataSign (base64 RSA signature over the dataBase64 string).</summary>
    public string? EvidenceDataSign { get; set; }

    /// <summary>SHA-256 hex digest of the dataBase64 string — re-verify without storing payload.</summary>
    public string? EvidencePayloadDigest { get; set; }

    /// <summary>
    /// dataBase64 payload encrypted at rest with AES-256-GCM (key from env
    /// <c>Ekyc:EncryptionKeyBase64</c>, never DB). Retention-limited legal evidence.
    /// </summary>
    public byte[]? EvidencePayloadFull { get; set; }

    /// <summary>HMAC-SHA256 hex of the normalized national ID with server secret — dedupe lookup.</summary>
    public string? IdentificationHmac { get; set; }

    /// <summary>Declared full name captured at intake (reception-assisted path). OCR must match.</summary>
    public string? DeclaredFullName { get; set; }

    /// <summary>Declared date of birth captured at intake. OCR must match.</summary>
    public DateOnly? DeclaredBirthDay { get; set; }

    /// <summary>HMAC of the declared CCCD (never plaintext). OCR identification must match.</summary>
    public string? DeclaredIdentificationHmac { get; set; }

    public string? ConsentVersion { get; set; }
    public DateTime? ConsentAcceptedAt { get; set; }
    public System.Net.IPAddress? ClientIp { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>
    /// Staff who operated a RECEPTION_ASSISTED session. NULL for self-service. Must differ from
    /// the subject account (<see cref="UserId"/>) — no self-verification.
    /// </summary>
    public long? PerformedBy { get; set; }

    public string? FailureReason { get; set; }
    public int AttemptCount { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Single-use enforcement — set when the session is consumed by activation/identity verification.</summary>
    public DateTime? ConsumedAt { get; set; }

    /// <summary>
    /// Step-binding validation state (migration 030). PENDING until a compare reference is
    /// observed; NOT_APPLICABLE when the optional compare step was not run; VALID when the compare
    /// document hash matches a session-uploaded hash; INVALID_BINDING on mismatch,
    /// UNVERIFIABLE_BINDING when unparseable — both fail-closed.
    /// </summary>
    public string BindingState { get; set; } = EkycBindingStates.Pending;

    /// <summary>Document object-hash the first face-compare call referenced (evidence for binding audit).</summary>
    public string? CompareReferenceHash { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
