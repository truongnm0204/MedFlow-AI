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
    public const string CancelledRefundPending = "CANCELLED_REFUND_PENDING";
}

public static class PaymentScopes
{
    public const string Charge = "CHARGE";
    public const string LegacyVisit = "LEGACY_VISIT";
}
