namespace MedFlow.Application.DTOs.Doctors;

public sealed record DoctorDetailDto(
    string Id,
    string DoctorCode,
    string StaffCode,
    string Name,
    string Title,
    string RoleTitle,
    string Specialty,
    string SpecialtyCode,
    string DepartmentName,
    string Room,
    string StatusText,
    decimal Price,
    string AvatarUrl,
    int YearsOfExperience,
    string? LicenseNumber,
    IReadOnlyList<DoctorEducationItemDto> Education,
    string Biography,
    IReadOnlyList<string> Hashtags,
    IReadOnlyList<DoctorWeeklyScheduleDayDto> WeeklySchedule);
