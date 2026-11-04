namespace MedFlow.Domain.Entities;

public class Visit
{
    public long Id { get; set; }
    public string? VisitCode { get; set; }
    public long? PatientId { get; set; }
    public long? AppointmentId { get; set; }
    public long? AssignedDoctorId { get; set; }
    public long? ExaminationRoomId { get; set; }
    public long? CheckedInByStaffId { get; set; }
    public string? ReasonForVisit { get; set; }
    public string? Status { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? CompletedByDoctorId { get; set; }
}
