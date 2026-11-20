namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record StaffAccountRoleDto(
    long RoleId,
    string Code,
    string Name
);
