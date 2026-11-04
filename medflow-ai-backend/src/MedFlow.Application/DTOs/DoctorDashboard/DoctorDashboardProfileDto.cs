namespace MedFlow.Application.DTOs.DoctorDashboard;

public sealed record DoctorDashboardProfileDto(
    long UserId,
    long DoctorId,
    string DoctorCode,
    string? StaffCode,
    string Email,
    string FullName,
    string? ProfessionalTitle,
    string DisplayName,
    string? SpecialtyName,
    long? DepartmentId,
    string DepartmentName,
    long HospitalId,
    string HospitalCode,
    string HospitalName,
    string RoomCode,
    string RoomName,
    string? LicenseNumber,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions
);
