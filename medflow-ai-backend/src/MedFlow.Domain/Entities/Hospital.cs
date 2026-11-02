namespace MedFlow.Domain.Entities;

public class Hospital
{
    public long Id { get; set; }
    public string HospitalCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool? IsActive { get; set; }
}
