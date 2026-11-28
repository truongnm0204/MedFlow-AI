namespace MedFlow.Application.Options;

/// <summary>
/// VNPT eKYC integration settings (bound to <c>Ekyc</c> section).
/// Secrets come from environment / secret manager only — NEVER database, NEVER committed files:
/// <list type="bullet">
///   <item><c>EncryptionKeyBase64</c> — 32-byte AES-256 key (base64) for evidence_payload_full at-rest encryption.</item>
///   <item><c>HmacKey</c> — server secret for identification HMAC dedupe. Falls back to <c>Identity:HashKey</c> when unset.</item>
///   <item><c>TokenId</c> / <c>TokenKey</c> / <c>AccessToken</c> / <c>PublicKeyCa</c> — VNPT credentials (env <c>VNPT_EKYC_*</c>).</item>
/// </list>
/// Non-secret knobs (<c>CompareThreshold</c>, <c>SessionTtlMinutes</c>, <c>MaxAttempts</c>, <c>BackendUrl</c>)
/// are config-driven per hard requirement 7 — never hardcoded in service logic.
/// </summary>
public sealed class EkycOptions
{
    public const string SectionName = "Ekyc";

    /// <summary>VNPT AI gateway base URL.</summary>
    public string BackendUrl { get; set; } = "https://api.idg.vnpt.vn";

    /// <summary>Minimum face-compare prob for a positive verification verdict. Never hardcoded in logic —
    /// tune via config after volunteer testing (plan v2.1 mục 8). Scale follows VNPT compare.prob percentage (0-100).</summary>
    public double CompareThreshold { get; set; } = 70.0;

    /// <summary>eKYC session TTL in minutes (hard requirement 4: short-lived).</summary>
    public int SessionTtlMinutes { get; set; } = 15;

    /// <summary>Max step re-submission attempts per session before forced rejection.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Maximum eKYC sessions a subject may start per day (owner scope: mặc định 3). Applies to
    /// self-service and to sessions bound to a patient with an account. 0 disables.
    /// </summary>
    public int DailySessionQuotaPerSubject { get; set; } = 3;

    /// <summary>
    /// Maximum reception-assisted eKYC sessions one staff member may start per day (ca ≈ ngày).
    /// 0 disables. Exceeding either quota returns code QUOTA_EXHAUSTED (429) + audit.
    /// </summary>
    public int DailySessionQuotaPerStaff { get; set; } = 20;

    /// <summary>
    /// Currently active consent text version (Luật 91/2025 + NĐ 356/2025 versioning). Session creation
    /// requires the client to accept exactly this version — bumping the version invalidates older consents.
    /// Non-secret operational config.
    /// </summary>
    public string ConsentVersion { get; set; } = "ekyc-consent-v1";

    /// <summary>Upstream timeout (seconds) for one VNPT proxy call. No retries — see service docs.</summary>
    public int UpstreamTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Feature flag (plan v2.1 mục 3.13): when OFF (default) the registration flow is exactly the
    /// pre-eKYC behavior — OTP verify activates the account immediately. When ON, OTP verify keeps
    /// the account PENDING and issues a registration-scope token; activation happens only after a
    /// successful eKYC finalize + dedupe resolution. Wired end-to-end in slice 6; this slice
    /// implements the mechanism so the flag can be flipped without further code changes.
    /// Non-secret operational config.
    /// </summary>
    public bool MandatoryRegistration { get; set; } = false;

    /// <summary>SECRET — base64-encoded 32-byte AES-256 key. From env/secret store only.</summary>
    public string? EncryptionKeyBase64 { get; set; }

    /// <summary>SECRET — HMAC key for identification dedupe. Falls back to Identity:HashKey when unset.</summary>
    public string? HmacKey { get; set; }

    /// <summary>SECRET — VNPT Token-id header credential.</summary>
    public string? TokenId { get; set; }

    /// <summary>SECRET — VNPT Token-key header credential.</summary>
    public string? TokenKey { get; set; }

    /// <summary>SECRET — VNPT Bearer access token (JWT, short-lived per probe Q5).</summary>
    public string? AccessToken { get; set; }

    /// <summary>SECRET — VNPT CA public key (base64 DER/SPKI) for server-side dataSign verification.</summary>
    public string? PublicKeyCa { get; set; }
}
