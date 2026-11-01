namespace MedFlow.Domain.Entities;

public class Doctor
{
    public long Id { get; set; }
    public long? StaffId { get; set; }
    public long? HospitalId { get; set; }
    public string DoctorCode { get; set; } = string.Empty;
    public long? SpecialtyId { get; set; }
    public string? ProfessionalTitle { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ProfileImageUrl { get; set; }
    public short? YearsOfExperience { get; set; }
    public string? LicenseNumber { get; set; }
    public string? ProfessionalBio { get; set; }
    public string? QualificationsSummary { get; set; }

    public Staff? Staff { get; set; }
    public Specialty? Specialty { get; set; }
}

