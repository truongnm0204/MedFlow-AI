namespace MedFlow.Application.DTOs.Diagnostic;

public sealed record DiagnosticStaffProfileDto(
    long UserId,
    string Email,
    string FullName,
    string? StaffCode,
    long HospitalId,
    string HospitalCode,
    string HospitalName,
    long? DepartmentId,
    string DepartmentName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions
);
