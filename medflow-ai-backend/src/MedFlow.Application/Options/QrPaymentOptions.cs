namespace MedFlow.Application.Options;

/// <summary>
/// Configurable QR payment request expiry settings (bound to <c>QrPayment</c> section).
/// </summary>
public sealed class QrPaymentOptions
{
    public const string SectionName = "QrPayment";

    public int PendingExpiryMinutes { get; set; } = 15;
}
