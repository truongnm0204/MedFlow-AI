namespace MedFlow.Application.Services.Payments;

/// <summary>Kết quả đối soát của một giao dịch cổng thanh toán.</summary>
public static class PaymentGatewayOutcomes
{
    public const string Ignored = "IGNORED";
    public const string Unmatched = "UNMATCHED";
    public const string Paid = "PAID";
    public const string Partial = "PARTIAL";
    public const string Overpaid = "OVERPAID";
    public const string AlreadyPaid = "ALREADY_PAID";
    public const string AlreadyClosed = "ALREADY_CLOSED";

    public static readonly string[] Counted = [Paid, Partial, Overpaid];
}
