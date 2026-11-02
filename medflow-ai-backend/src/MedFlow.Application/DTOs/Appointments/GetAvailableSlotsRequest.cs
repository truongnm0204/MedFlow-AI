namespace MedFlow.Application.DTOs.Appointments;

public class GetAvailableSlotsRequest
{
    public string? DoctorId { get; set; }
    public string? ServiceId { get; set; }
    public string? DateIso { get; set; }
}
