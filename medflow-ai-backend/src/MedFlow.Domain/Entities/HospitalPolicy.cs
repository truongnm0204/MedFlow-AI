namespace MedFlow.Domain.Entities;

public class HospitalPolicy
{
    public long Id { get; set; }
    public long? HospitalId { get; set; }
    public long? QueueServicePointId { get; set; }
    public string PolicyType { get; set; } = string.Empty;
    public string PolicyKey { get; set; } = string.Empty;
    public string? PolicyValue { get; set; }
    public int? PolicyVersion { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}
