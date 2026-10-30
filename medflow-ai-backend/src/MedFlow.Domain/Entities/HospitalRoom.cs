namespace MedFlow.Domain.Entities;

public class HospitalRoom
{
    public long Id { get; set; }
    public long? HospitalId { get; set; }
    public long? DepartmentId { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RoomType { get; set; } = string.Empty;
    public string? Location { get; set; }
    public int? Capacity { get; set; }
    public string Status { get; set; } = string.Empty;

    public Department? Department { get; set; }
}
