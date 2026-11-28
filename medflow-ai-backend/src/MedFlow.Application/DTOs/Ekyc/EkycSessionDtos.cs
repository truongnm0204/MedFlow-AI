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
    string VerificationMethod,
    string BindingState,
    int AttemptCount,
    DateTime ExpiresAt,
    bool Expired,
    string? FailureReason,
    DateTime CreatedAt);

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
    string? ActivationReviewReason = null);
