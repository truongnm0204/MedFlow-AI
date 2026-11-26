namespace MedFlow.Domain.Constants;

public static class VisitChargeTypes
{
    public const string Examination = "EXAMINATION";
    public const string Diagnostic = "DIAGNOSTIC";
}

public static class VisitChargeStatuses
{
    public const string Active = "ACTIVE";
    public const string Cancelled = "CANCELLED";
    public const string PaidNotPerformed = "PAID_NOT_PERFORMED";
    public const string CancelledRefundPending = "CANCELLED_REFUND_PENDING";
}

public static class PaymentScopes
{
    public const string Charge = "CHARGE";
    public const string Allocated = "ALLOCATED";
    public const string LegacyVisit = "LEGACY_VISIT";
}

public static class PaymentMethods
{
    public const string VietQr = "VIETQR";
    public const string Cash = "CASH";
}

public static class PaymentAllocationStatuses
{
    public const string Pending = "PENDING";
    public const string Paid = "PAID";
    public const string Expired = "EXPIRED";
    public const string Cancelled = "CANCELLED";
    public const string Reconcile = "RECONCILE";
}

public static class DiagnosticSelectionStatuses
{
    public const string Undecided = "UNDECIDED";
    public const string Accepted = "ACCEPTED";
    public const string Declined = "DECLINED";
    public const string ReviewRequired = "REVIEW_REQUIRED";
    public const string SkippedAfterPaid = "SKIPPED_AFTER_PAID";
}
