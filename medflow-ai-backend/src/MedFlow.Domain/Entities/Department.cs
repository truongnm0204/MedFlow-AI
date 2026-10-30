namespace MedFlow.Domain.Entities;

public class Department
{
    public long Id { get; set; }
    public long? HospitalId { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool? IsActive { get; set; }

    public ICollection<Service> Services { get; set; } = new List<Service>();
}
