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
    public DateOnly? LastPresentDate { get; set; }
    public string PresenceState { get; set; } = "PRESENT";
    public DateTime? LastResumedAt { get; set; }
    public DateTime? ReadyForReturnAt { get; set; }
    public long? OverdueReturnApprovedByDoctorId { get; set; }
    public DateTime? OverdueReturnApprovedAt { get; set; }
    public string? OverdueReturnApprovalNote { get; set; }
    public string? OverdueReturnResultSnapshot { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? CompletedByDoctorId { get; set; }
}
