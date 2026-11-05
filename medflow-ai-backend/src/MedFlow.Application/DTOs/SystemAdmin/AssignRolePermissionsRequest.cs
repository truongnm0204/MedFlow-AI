namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record AssignRolePermissionsRequest(
    string? RoleCode = null,
    IReadOnlyList<string>? PermissionCodes = null,
    string? Note = null
);
