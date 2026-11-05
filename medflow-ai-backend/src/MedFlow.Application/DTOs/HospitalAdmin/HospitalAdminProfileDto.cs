namespace MedFlow.Application.DTOs.HospitalAdmin;

public sealed record HospitalAdminProfileDto(
    long UserId,
    string Email,
    string FullName,
    string? StaffCode,
    long HospitalId,
    string HospitalCode,
    string HospitalName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions
);
