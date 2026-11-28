namespace MedFlow.Domain.Constants;

/// <summary>
/// eKYC verification session state machine (plan v2.1 mục 2).
/// Step ordering enforced server-side: INITIATED → DOCUMENT_FRONT_DONE → DOCUMENT_BACK_DONE
/// → FACE_DONE → FINALIZED → terminal (VERIFIED / REJECTED). EXPIRED by TTL sweep,
/// CONSUMED after single-use activation/consume.
/// Face steps are optional in the owner-approved CCCD-only policy; the intermediate states
/// remain for SDK tracing.
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

/// <summary>
/// How the session was operated. Replaces the old EKYC_VNPT/RECEPTION_MANUAL discriminator:
/// there is exactly ONE verification engine (VNPT eKYC); reception assistance only changes who
/// drives it and which identity artifact (account vs patient profile) is the result.
/// </summary>
public static class EkycVerificationModes
{
    public const string SelfService = "SELF_SERVICE";
    public const string ReceptionAssisted = "RECEPTION_ASSISTED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SelfService, ReceptionAssisted,
    };
}

/// <summary>Identity artifact the session produces — patient identity is separate from account activation.</summary>
public static class EkycPurposes
{
    public const string AccountActivation = "ACCOUNT_ACTIVATION";
    public const string PatientProfile = "PATIENT_PROFILE";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        AccountActivation, PatientProfile,
    };
}

/// <summary>Step-binding validation states (migration 030) — fail-closed semantics.</summary>
public static class EkycBindingStates
{
    /// <summary>No compare reference observed yet.</summary>
    public const string Pending = "PENDING";

    /// <summary>Face compare was not run (optional in CCCD-only policy) — binding not required.</summary>
    public const string NotApplicable = "NOT_APPLICABLE";

    /// <summary>Compare document hash matches a hash uploaded within this session.</summary>
    public const string Valid = "VALID";

    /// <summary>Compare document hash does NOT match any session-uploaded hash — composite-attack signal.</summary>
    public const string Invalid = "INVALID_BINDING";

    /// <summary>Compare reference could not be extracted/parsed — cannot prove binding, treat as failure.</summary>
    public const string Unverifiable = "UNVERIFIABLE_BINDING";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Pending, NotApplicable, Valid, Invalid, Unverifiable,
    };
}

/// <summary>Patient identity verification status — VERIFIED is written ONLY by successful eKYC finalize.</summary>
public static class PatientIdentityStatuses
{
    public const string Verified = "VERIFIED";
    public const string Unverified = "UNVERIFIED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Verified, Unverified,
    };
}

/// <summary>
/// Owner-approved exceptions that allow creating a patient profile WITHOUT eKYC.
/// Every such creation requires one of these reasons + audit; dedupe still runs first.
/// </summary>
public static class PatientUnverifiedReasons
{
    public const string Child = "CHILD";
    public const string Foreigner = "FOREIGNER";
    public const string NoValidDocument = "NO_VALID_DOCUMENT";
    public const string ConsentDeclined = "CONSENT_DECLINED";
    public const string VnptUnavailable = "VNPT_UNAVAILABLE";
    public const string QuotaExhausted = "QUOTA_EXHAUSTED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Child, Foreigner, NoValidDocument, ConsentDeclined, VnptUnavailable, QuotaExhausted,
    };

    /// <summary>
    /// Reasons that imply the person HAS a valid CCCD (the engine could not run for operational
    /// reasons) — a declared CCCD is required so dedupe can run.
    /// </summary>
    public static readonly IReadOnlySet<string> RequireDeclaredIdentification = new HashSet<string>(StringComparer.Ordinal)
    {
        ConsentDeclined, VnptUnavailable, QuotaExhausted,
    };
}

/// <summary>Machine-readable eKYC failure codes surfaced to clients (ProblemDetails extension `code`).</summary>
public static class EkycErrorCodes
{
    public const string VnptUnavailable = "VNPT_UNAVAILABLE";
    public const string QuotaExhausted = "QUOTA_EXHAUSTED";
}

public static class EkycConstants
{
    /// <summary>Max length of consent version tag (varchar(32) in schema).</summary>
    public const int ConsentVersionMaxLength = 32;

    /// <summary>Max length of a declared full name (patients.full_name is varchar(200)).</summary>
    public const int DeclaredFullNameMaxLength = 200;

    /// <summary>Audit actions (audit_logs.action is free-text — no schema change required).</summary>
    public const string AuditActionSessionCreated = "EKYC_SESSION_CREATED";
    public const string AuditActionStepSubmitted = "EKYC_STEP_SUBMITTED";
    public const string AuditActionVerified = "EKYC_VERIFIED";
    public const string AuditActionRejected = "EKYC_REJECTED";
    public const string AuditActionQuotaExceeded = "EKYC_QUOTA_EXCEEDED";
    public const string AuditActionClaimEkycMatched = "CLAIM_EKYC_MATCHED";

    /// <summary>Reception-assisted session created/operated by staff (owner audit requirement).</summary>
    public const string AuditActionReceptionAssisted = "RECEPTION_EKYC_ASSISTED";

    /// <summary>Patient profile created without eKYC — always carries a reason in change_data.</summary>
    public const string AuditActionPatientProfileCreatedUnverified = "PATIENT_PROFILE_CREATED_UNVERIFIED";

    public const string AuditEntityType = "EKYC_VERIFICATION_SESSION";
    public const string AuditEntityTypePatient = "PATIENT";
}
