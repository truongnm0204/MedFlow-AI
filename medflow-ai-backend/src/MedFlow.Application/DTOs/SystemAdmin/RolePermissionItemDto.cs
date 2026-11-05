namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record RolePermissionItemDto(
    long PermissionId,
    string Code,
    string Name,
    string Module
);
