namespace MedFlow.Application.DTOs.Doctors;

public sealed record DoctorAdminListItemDto(
    long Id,
    string DoctorCode,
    long? StaffId,
    string? StaffCode,
    string FullName,
    string? Email,
    string? Phone,
    string? Degree,
    long? SpecialtyId,
    string? SpecialtyName,
    long? DepartmentId,
    string? DepartmentName,
    string? LicenseNumber,
    short? YearsOfExperience,
    bool IsActive,
    string? ProfileImageUrl,
    string? ProfessionalBio,
    string? QualificationsSummary
);

