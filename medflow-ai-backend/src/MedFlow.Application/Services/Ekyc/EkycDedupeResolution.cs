namespace MedFlow.Application.Services.Ekyc;

/// <summary>
/// Dedupe matrix outcomes (owner 3.1, plan v2.1 mục 3.3) — machine-readable codes surfaced in the
/// finalize response so the client can route the user correctly. EVERY case has an explicit
/// outcome; NONE auto-merges.
/// </summary>
public static class EkycDedupeOutcomes
{
    /// <summary>Case 1: no existing patient with this identification — verified identity written
    /// onto the registration-created patient record. Account ACTIVATED.</summary>
    public const string NewIdentityActivated = "NEW_IDENTITY_ACTIVATED";

    /// <summary>Case 2: existing patient with this identification, never had an account — claim
    /// link: user is re-pointed at the pre-existing record, the empty registration shell is
    /// unlinked and voided (no medical data moved — shells are empty by construction).
    /// Account ACTIVATED. Audit CLAIM_EKYC_MATCHED.</summary>
    public const string ClaimLinkedActivated = "CLAIM_LINKED_ACTIVATED";

    /// <summary>Case 3: the identified patient is managed under a family link — the registrant IS
    /// the verified person, but ownership of the managed record requires reception review
    /// (eKYC never auto-approves claim/link). Account ACTIVATED with the registration shell;
    /// identity linkage PENDING reception review.</summary>
    public const string FamilyManagedPendingReview = "FAMILY_MANAGED_PENDING_REVIEW";

    /// <summary>Case 4: the identification is already bound to a DIFFERENT user account — activation
    /// REFUSED. Account stays PENDING; user is directed to account recovery. Audit written.</summary>
    public const string ConflictAccountExists = "CONFLICT_ACCOUNT_EXISTS";

    /// <summary>Case 5: identification matches an existing patient but name/DOB disagree — manual
    /// review. Account ACTIVATED (identity IS verified) but identity linkage PENDING review.</summary>
    public const string InfoMismatchPendingReview = "INFO_MISMATCH_PENDING_REVIEW";

    /// <summary>Case 6: multiple patient rows carry the same identification — data conflict,
    /// NEVER auto-merge. Manual review. Account ACTIVATED with registration shell; linkage PENDING.</summary>
    public const string MultipleRecordsConflict = "MULTIPLE_RECORDS_CONFLICT";

    /// <summary>Activation not applicable — account was already ACTIVE (mature-account eKYC) or
    /// the mandatory-registration flag is OFF. No dedupe/activation work performed.</summary>
    public const string NotApplicable = "NOT_APPLICABLE";
}

/// <summary>Result of one dedupe+activation resolution inside the finalize transaction.</summary>
public sealed record EkycDedupeResolution(
    string Outcome,
    bool AccountActivated,
    long? LinkedPatientId,
    string? ReviewReason);
