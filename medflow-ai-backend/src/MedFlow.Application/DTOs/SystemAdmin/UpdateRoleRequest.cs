namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record UpdateRoleRequest(
    string? Code = null,
    string? Name = null,
    string? Description = null,
    string? Scope = null,
    string? Status = null,
    bool? IsActive = null,
    string? SegregationNote = null,
    IReadOnlyList<string>? PermissionCodes = null,
    string? Note = null,
    string? Reason = null,
    string? RoleCode = null,
    string? RoleName = null
);
