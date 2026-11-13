namespace MedFlow.Domain.Entities;

public class Payment
{
    public long Id { get; set; }
    public string? PaymentCode { get; set; }
    public long? VisitId { get; set; }
    public decimal? Amount { get; set; }
    public string? ExternalReference { get; set; }
    public string? Status { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
