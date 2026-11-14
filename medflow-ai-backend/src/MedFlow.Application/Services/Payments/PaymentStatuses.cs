namespace MedFlow.Application.Services.Payments;

/// <summary>Trạng thái của `payments.status` (varchar(16)).</summary>
public static class PaymentStatuses
{
    public const string Pending = "PENDING";
    public const string Paid = "PAID";
    public const string Failed = "FAILED";
    public const string Expired = "EXPIRED";
    public const string Cancelled = "CANCELLED";
}
