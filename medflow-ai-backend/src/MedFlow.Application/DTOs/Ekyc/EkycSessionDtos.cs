namespace MedFlow.Application.DTOs.Ekyc;

/// <summary>
/// Session creation request. Consent is MANDATORY before any image upload (hard requirement 11 —
/// consent capture TRƯỚC upload ảnh đầu tiên, Luật 91/2025 + NĐ 356/2025).
/// </summary>
public sealed record CreateEkycSessionRequest
{
    /// <summary>Consent text version the user accepted — must match the active configured version.</summary>
    public string? ConsentVersion { get; init; }

    /// <summary>Explicit acceptance flag — must be true. Server records acceptance timestamp, never trusts client clocks alone.</summary>
    public bool ConsentAccepted { get; init; }

    /// <summary>
    /// Declared CCCD/CMND number (9 or 12 digits). Required: the OCR number must match what the
    /// person declared (owner scope 2026-11-28), and the plaintext value is never stored — only a
    /// server-keyed HMAC is persisted for matching/dedupe.
    /// </summary>
    public string? IdentityCard { get; init; }
}

public sealed record CreateEkycSessionResponse(
    string SessionId,
    string ProxyBasePath,
    string Status,
    DateTime ExpiresAt);

/// <summary>Session status view — never leaks evidence payloads, OCR PII beyond state, or secrets.</summary>
public sealed record EkycSessionStatusResponse(
    string SessionId,
    string Status,
    string VerificationMode,
    string Purpose,
    string BindingState,
    int AttemptCount,
    DateTime ExpiresAt,
    bool Expired,
    string? FailureReason,
    DateTime CreatedAt);

/// <summary>
/// Reception-assisted session creation: staff drives the same engine for an existing patient
/// profile (new or still UNVERIFIED). Permission PATIENT_IDENTITY_VERIFY is enforced in the
/// service layer; performed_by is recorded on the session.
/// </summary>
public sealed record CreateReceptionEkycSessionRequest
{
    public long PatientId { get; init; }

    public string? ConsentVersion { get; init; }

    public bool ConsentAccepted { get; init; }

    /// <summary>
    /// CCCD declared by the patient at the counter. Optional only when the patient record already
    /// carries a declared identification HMAC; the OCR number must match it.
    /// </summary>
    public string? IdentityCard { get; init; }
}

/// <summary>
/// Create a patient profile WITHOUT eKYC — owner-approved exception path. Requires one of
/// <see cref="MedFlow.Domain.Constants.PatientUnverifiedReasons"/>; dedupe runs first.
/// </summary>
public sealed record CreateUnverifiedPatientProfileRequest
{
    public string? FullName { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public string? Email { get; init; }

    /// <summary>Declared CCCD (9/12 digits) — required for reasons that imply a valid card; never stored plaintext.</summary>
    public string? IdentityCard { get; init; }

    /// <summary>One of CHILD / FOREIGNER / NO_VALID_DOCUMENT / CONSENT_DECLINED / VNPT_UNAVAILABLE / QUOTA_EXHAUSTED.</summary>
    public string? Reason { get; init; }

    public string? Note { get; init; }
}

public sealed record CreateUnverifiedPatientProfileResponse(
    long PatientId,
    string PatientCode,
    string FullName,
    string IdentityStatus,
    string UnverifiedReason);

/// <summary>
/// Finalize response — the verdict is fully backend-decided (TRUST ANCHOR: the client never
/// submits verdicts). On rejection the reason is a coarse machine-readable code plus a
/// user-safe message — never evidence payloads, never OCR PII, never internals beyond need-to-know.
/// </summary>
public sealed record FinalizeEkycSessionResponse(
    string SessionId,
    string Status,
    string? FailureReason,
    string? FailureCode,
    DateTime? ConsumedAt,
    bool? AccountActivated = null,
    string? ActivationOutcome = null,
    string? ActivationReviewReason = null,
    string? Purpose = null,
    long? PatientId = null,
    string? PatientIdentityStatus = null);
