namespace MedFlow.Domain.Entities;

public class Patient
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string PatientCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    /// <summary>SHA-256 HMAC hash of identity number — never store plaintext.</summary>
    public string? IdentificationHash { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string? Address { get; set; }
    public string? BloodType { get; set; }

    public User? User { get; set; }
}
