namespace MedFlow.Domain.Entities;

public class VisitCharge
{
    public long Id { get; set; }
    public long? VisitId { get; set; }
    public long? ServiceId { get; set; }
    public long? DiagnosticOrderId { get; set; }
    /// <summary>Line amount. visit_charges has no quantity column.</summary>
    public decimal? UnitPrice { get; set; }
    public string? PaymentRequirement { get; set; }
}
