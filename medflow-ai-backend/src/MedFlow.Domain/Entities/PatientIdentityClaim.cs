namespace MedFlow.Domain.Entities;

/// <summary>Yêu cầu liên kết tài khoản với hồ sơ bệnh nhân đã có (ADR 0044).</summary>
public class PatientIdentityClaim
{
    public long Id { get; set; }
    public long RequesterUserId { get; set; }
    public long? CurrentPatientId { get; set; }
    public long? TargetPatientId { get; set; }
    public string RequestedPatientCode { get; set; } = string.Empty;
    public long HospitalId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public long? DecidedByStaffId { get; set; }
    public string? DecisionNote { get; set; }
}
