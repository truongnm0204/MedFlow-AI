namespace MedFlow.Domain.Constants;

public static class PaymentEnrollmentIntentStatuses
{
    public const string PendingSettlement = "PENDING_SETTLEMENT";
    public const string PendingPostCommit = "PENDING_POST_COMMIT";
    public const string ConsumedEnrolled = "CONSUMED_ENROLLED";
    public const string SkippedNotEligible = "SKIPPED_NOT_ELIGIBLE";
    public const string Cancelled = "CANCELLED";
    public const string Expired = "EXPIRED";

    public static bool IsTerminal(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.Trim().ToUpperInvariant() is ConsumedEnrolled or SkippedNotEligible or Cancelled or Expired;
    }
}

public static class PaymentEnrollmentIntentOutcomeCodes
{
    public const string Enrolled = "ENROLLED";
    public const string PatientNotPresent = "PATIENT_NOT_PRESENT";
    public const string ClinicalHandoffRequired = "CLINICAL_HANDOFF_REQUIRED";
    public const string ActiveQueueAlreadyExists = "ACTIVE_QUEUE_ALREADY_EXISTS";
    public const string NoActiveCompatibleDiagnosticPoint = "NO_ACTIVE_COMPATIBLE_DIAGNOSTIC_POINT";
    public const string DiagnosticOrderNotEligible = "DIAGNOSTIC_ORDER_NOT_ELIGIBLE";
    public const string DiagnosticOrderExpired = "DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED";
    public const string UnpaidDiagnosticFee = "UNPAID_DIAGNOSTIC_FEE";
    public const string IntentExpired = "INTENT_EXPIRED";
    public const string IntentCancelled = "INTENT_CANCELLED";
    public const string TransientConflictMaxRetries = "TRANSIENT_CONFLICT_MAX_RETRIES";
}
