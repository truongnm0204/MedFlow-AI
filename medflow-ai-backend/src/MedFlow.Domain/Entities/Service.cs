namespace MedFlow.Domain.Entities;

public class Service
{
    public long Id { get; set; }
    public long? HospitalId { get; set; }
    public long? DepartmentId { get; set; }
    public long? SpecialtyId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool? AllowsOnlineBooking { get; set; }
    public bool? RequiresResultVerification { get; set; }
    public bool? IsActive { get; set; }

    public Department? Department { get; set; }
    public Specialty? Specialty { get; set; }
    public ICollection<ServicePrice> ServicePrices { get; set; } = new List<ServicePrice>();
}
