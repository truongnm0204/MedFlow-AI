namespace MedFlow.Domain.Entities;

public class Specialty
{
    public long Id { get; set; }
    public string SpecialtyCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool? IsActive { get; set; }

    public ICollection<Service> Services { get; set; } = new List<Service>();
}
