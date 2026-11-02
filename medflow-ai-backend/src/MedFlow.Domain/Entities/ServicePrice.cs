namespace MedFlow.Domain.Entities;

public class ServicePrice
{
    public long Id { get; set; }
    public long? ServiceId { get; set; }
    public decimal? PriceAmount { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }

    public Service? Service { get; set; }
}
