namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record SystemAdminProfileDto(
    long UserId,
    string Email,
    string FullName,
    string? StaffCode,
    long? HospitalId,
    string HospitalName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions
);
