namespace MedFlow.Domain.Entities;

public class PatientFamilyLink
{
    public long OwnerPatientId { get; set; }
    public long MemberPatientId { get; set; }
    public string? Relationship { get; set; }
    public string? VerificationStatus { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? ValidUntil { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Patient? OwnerPatient { get; set; }
    public Patient? MemberPatient { get; set; }
}
