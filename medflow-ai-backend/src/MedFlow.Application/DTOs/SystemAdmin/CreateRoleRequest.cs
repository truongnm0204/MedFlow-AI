namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record CreateRoleRequest(
    string? Code = null,
    string? Name = null,
    string? Description = null,
    string? Scope = null,
    string? Status = null,
    string? SegregationNote = null,
    IReadOnlyList<string>? PermissionCodes = null,
    string? Note = null,
    string? RoleCode = null,
    string? RoleName = null
);
