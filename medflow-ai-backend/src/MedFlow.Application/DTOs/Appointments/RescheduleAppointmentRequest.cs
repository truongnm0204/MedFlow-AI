namespace MedFlow.Application.DTOs.Appointments;

public class RescheduleAppointmentRequest
{
    public string? TargetSlotId { get; set; }
    public string? RescheduleNote { get; set; }
}
