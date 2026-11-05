namespace MedFlow.Application.DTOs.Doctors;

public sealed record UpdateDoctorAdminRequest(
    long? SpecialtyId = null,
    string? Specialty = null,
    long? DepartmentId = null,
    string? Department = null,
    string? ProfessionalTitle = null,
    string? Degree = null,
    bool? IsActive = null,
    string? Status = null,
    string? FullName = null,
    string? Name = null,
    string? LicenseNumber = null,
    string? License = null,
    short? YearsOfExperience = null,
    string? ProfileImageUrl = null,
    string? ProfessionalBio = null,
    string? Biography = null,
    string? QualificationsSummary = null,
    string? Reason = null
);
