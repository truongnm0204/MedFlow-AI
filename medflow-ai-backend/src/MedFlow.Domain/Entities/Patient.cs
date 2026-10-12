namespace MedFlow.Domain.Entities;

public class Patient
{
    public Guid Id { get; set; }
    public string PatientCode { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? IdentificationNo { get; set; }
    public string? Address { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string? AvatarUrl { get; set; }
    public string? Nationality { get; set; }
    public string? BloodType { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    public DateOnly? IdentityIssuedDate { get; set; }
    public string? IdentityIssuedPlace { get; set; }

    public User? User { get; set; }
}
