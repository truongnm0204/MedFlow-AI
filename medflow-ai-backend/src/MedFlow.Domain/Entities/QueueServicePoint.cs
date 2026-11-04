namespace MedFlow.Domain.Entities;

public class QueueServicePoint
{
    public long Id { get; set; }
    public long? HospitalId { get; set; }
    public long? RoomId { get; set; }
    public long? DoctorId { get; set; }
    public long? ServiceId { get; set; }
    public string? PointType { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
}
