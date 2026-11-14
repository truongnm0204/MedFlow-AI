namespace MedFlow.Application.DTOs.Payments;

/// <summary>Payload webhook SePay. Field nullable vì cổng có thể bỏ trống tuỳ ngân hàng.</summary>
public sealed class SePayWebhookRequest
{
    public long Id { get; set; }
    public string? Gateway { get; set; }
    public string? TransactionDate { get; set; }
    public string? AccountNumber { get; set; }
    public string? Code { get; set; }
    public string? Content { get; set; }
    public string? TransferType { get; set; }
    public decimal? TransferAmount { get; set; }
    public decimal? Accumulated { get; set; }
    public string? SubAccount { get; set; }
    public string? ReferenceCode { get; set; }
}

public sealed record SePayWebhookResult(bool Processed, string Outcome);
