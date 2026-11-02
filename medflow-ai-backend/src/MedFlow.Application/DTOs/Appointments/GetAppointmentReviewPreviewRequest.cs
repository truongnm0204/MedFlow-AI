namespace MedFlow.Application.DTOs.Appointments;

public class GetAppointmentReviewPreviewRequest
{
    public string? Mode { get; set; }
    public string? PatientId { get; set; }
    public string? DoctorId { get; set; }
    public string? ServiceId { get; set; }
    public string? ServiceCode { get; set; }
    public string? DateIso { get; set; }
    public string? SlotId { get; set; }
    public string? ShiftId { get; set; }
    public string? SymptomNote { get; set; }
}
