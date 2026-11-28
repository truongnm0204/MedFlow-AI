namespace MedFlow.Domain.Constants;

/// <summary>
/// eKYC verification session state machine (plan v2.1 mục 2).
/// Step ordering enforced server-side: INITIATED → DOCUMENT_FRONT_DONE → DOCUMENT_BACK_DONE
/// → FACE_DONE → FINALIZED → terminal (VERIFIED / REJECTED). EXPIRED by TTL sweep,
/// CONSUMED after single-use activation/consume.
/// </summary>
public static class EkycVerificationStatuses
{
    public const string Initiated = "INITIATED";
    public const string DocumentFrontDone = "DOCUMENT_FRONT_DONE";
    public const string DocumentBackDone = "DOCUMENT_BACK_DONE";
    public const string FaceDone = "FACE_DONE";
    public const string Finalized = "FINALIZED";
    public const string Verified = "VERIFIED";
    public const string Rejected = "REJECTED";
    public const string Expired = "EXPIRED";
    public const string Consumed = "CONSUMED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Initiated, DocumentFrontDone, DocumentBackDone, FaceDone, Finalized,
        Verified, Rejected, Expired, Consumed,
    };

    public static readonly IReadOnlySet<string> Terminal = new HashSet<string>(StringComparer.Ordinal)
    {
        Verified, Rejected, Expired, Consumed,
    };
}

/// <summary>Discriminator: online VNPT eKYC vs reception-assisted manual verification.</summary>
public static class EkycVerificationMethods
{
    public const string EkycVnpt = "EKYC_VNPT";
    public const string ReceptionManual = "RECEPTION_MANUAL";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        EkycVnpt, ReceptionManual,
    };
}

/// <summary>Step-binding validation states (migration 030) — fail-closed semantics.</summary>
public static class EkycBindingStates
{
    /// <summary>No compare reference observed yet.</summary>
    public const string Pending = "PENDING";

    /// <summary>Compare document hash matches a hash uploaded within this session.</summary>
    public const string Valid = "VALID";

    /// <summary>Compare document hash does NOT match any session-uploaded hash — composite-attack signal.</summary>
    public const string Invalid = "INVALID_BINDING";

    /// <summary>Compare reference could not be extracted/parsed — cannot prove binding, treat as failure.</summary>
    public const string Unverifiable = "UNVERIFIABLE_BINDING";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Pending, Valid, Invalid, Unverifiable,
    };
}

public static class EkycConstants
{
    /// <summary>Max length of consent version tag (varchar(32) in schema).</summary>
    public const int ConsentVersionMaxLength = 32;

    /// <summary>Audit actions (audit_logs.action is free-text — no schema change required).</summary>
    public const string AuditActionSessionCreated = "EKYC_SESSION_CREATED";
    public const string AuditActionStepSubmitted = "EKYC_STEP_SUBMITTED";
    public const string AuditActionVerified = "EKYC_VERIFIED";
    public const string AuditActionRejected = "EKYC_REJECTED";
    public const string AuditActionReceptionVerified = "EKYC_RECEPTION_VERIFIED";
    public const string AuditActionClaimEkycMatched = "CLAIM_EKYC_MATCHED";

    public const string AuditEntityType = "EKYC_VERIFICATION_SESSION";
}
