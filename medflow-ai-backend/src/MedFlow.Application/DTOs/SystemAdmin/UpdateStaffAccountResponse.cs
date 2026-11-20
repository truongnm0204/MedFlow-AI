namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record UpdateStaffAccountResponse(
    long UserId,
    long StaffId,
    string StaffCode,
    string FullName,
    string Email,
    string AuthProvider,
    string Status,
    string RoleCode,
    long? HospitalId,
    string? Facility,
    long? DepartmentId,
    string? Department,
    DateTime UpdatedAtUtc);
