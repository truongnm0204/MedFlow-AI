namespace MedFlow.Domain.Entities;

public class DoctorServiceAuthorization
{
    public long DoctorId { get; set; }
    public long ServiceId { get; set; }
    public bool? IsActive { get; set; }

    public Doctor? Doctor { get; set; }
    public Service? Service { get; set; }
}
