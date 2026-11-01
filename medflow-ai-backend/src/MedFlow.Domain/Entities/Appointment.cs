namespace MedFlow.Domain.Entities;

public class Appointment
{
    public long Id { get; set; }
    public string? AppointmentCode { get; set; }
    public long? PatientId { get; set; }
    public long? SlotId { get; set; }
    public string? BookingChannel { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? ReceptionNote { get; set; }
    public string? Status { get; set; }
    public long? BookedByUserId { get; set; }
    public DateTime? BookedAt { get; set; }

    public ExaminationSlot? Slot { get; set; }
    public Patient? Patient { get; set; }
}
