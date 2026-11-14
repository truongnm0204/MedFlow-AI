namespace MedFlow.Application.Services.Payments;

internal static class PaymentPolicyKeys
{
    public const string PolicyType = "PAYMENT";
    public const string VietQrAccount = "vietqr_account";
}

internal sealed record VietQrAccount(string Bin, string AccountNumber, string AccountName);
