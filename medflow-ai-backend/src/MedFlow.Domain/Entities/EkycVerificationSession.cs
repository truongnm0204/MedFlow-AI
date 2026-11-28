using MedFlow.Domain.Constants;

namespace MedFlow.Domain.Entities;

/// <summary>
/// One eKYC verification session — evidence record for both online VNPT eKYC
/// (<see cref="EkycVerificationMethods.EkycVnpt"/>) and reception-assisted manual
/// verification (<see cref="EkycVerificationMethods.ReceptionManual"/>).
/// Maps to <c>ekyc_verification_sessions</c> (migration 029).
/// NEVER stores raw document/face images: only OCR fields, verdicts, digests and the
/// AES-256-GCM encrypted VNPT payload snapshot.
/// </summary>
public class EkycVerificationSession
{
    public long Id { get; set; }

    /// <summary>Account being verified; null when the session was created pre-activation.</summary>
    public long? UserId { get; set; }

    /// <summary>
    /// Registration-scope session link. No FK — the registration-scope session mechanism
    /// (Phase 1 item 3) does not exist yet; reserved here to avoid a second schema touch.
    /// </summary>
    public long? RegistrationSessionId { get; set; }

    /// <summary>Linked patient profile once dedupe/claim resolves.</summary>
    public long? PatientId { get; set; }

    public string Status { get; set; } = EkycVerificationStatuses.Initiated;
    public string VerificationMethod { get; set; } = EkycVerificationMethods.EkycVnpt;

    /// <summary>OCR fields only (name/id/dob/... + prob scores). NEVER images.</summary>
    public string? OcrSnapshot { get; set; }

    /// <summary>Card front/back/face/masked liveness verdicts + messages.</summary>
    public string? LivenessVerdicts { get; set; }

    /// <summary>Face-vs-document compare result + prob.</summary>
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

    public string? ConsentVersion { get; set; }
    public DateTime? ConsentAcceptedAt { get; set; }
    public System.Net.IPAddress? ClientIp { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>Reception verifier. Must differ from <see cref="UserId"/> (no self-verification).</summary>
    public long? VerifiedBy { get; set; }

    public string? FailureReason { get; set; }
    public int AttemptCount { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Single-use enforcement — set when the session is consumed by activation/claim.</summary>
    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
