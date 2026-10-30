namespace MedFlow.Domain.Entities;

public class Staff
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public long? HospitalId { get; set; }
    public long? DepartmentId { get; set; }
    public string StaffCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = "ACTIVE";

    public User? User { get; set; }
    public Department? Department { get; set; }
    public Doctor? Doctor { get; set; }
}
