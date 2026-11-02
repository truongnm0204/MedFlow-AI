namespace MedFlow.Application.DTOs.Appointments;

public class GetServiceShiftsRequest
{
    public string? ServiceId { get; set; }
    public string? DateIso { get; set; }
}
