namespace MedFlow.Application.DTOs.Appointments;

public class CreateAppointmentRequest
{
    public string? Mode { get; set; }
    public string? PatientId { get; set; }
    public string? DoctorId { get; set; }
    public string? ServiceId { get; set; }
    public string? ServiceCode { get; set; }
    public string? DateIso { get; set; }
    public string? SlotOrShiftId { get; set; }
    public string? SymptomNote { get; set; }
}
