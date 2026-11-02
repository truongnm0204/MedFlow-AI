namespace MedFlow.Domain.Entities;

public class ExaminationSlot
{
    public long Id { get; set; }
    public long? ServiceId { get; set; }
    public long? DoctorId { get; set; }
    public long? RoomId { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int? Capacity { get; set; }
    public string Status { get; set; } = string.Empty;

    public Service? Service { get; set; }
    public Doctor? Doctor { get; set; }
    public HospitalRoom? Room { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
