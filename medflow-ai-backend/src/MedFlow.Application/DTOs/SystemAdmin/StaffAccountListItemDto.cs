namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record StaffAccountListItemDto(
    long StaffId,
    long UserId,
    string Id,
    string Code,
    string FullName,
    string Email,
    string RoleCode,
    string Role,
    IReadOnlyList<StaffAccountRoleDto> Roles,
    long? DepartmentId,
    string Department,
    long? HospitalId,
    string Facility,
    string Status
);
