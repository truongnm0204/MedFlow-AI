namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record RoleListItemDto(
    long RoleId,
    string Id,
    string Code,
    string Name,
    string Description,
    string Scope,
    bool IsSystemRole,
    int AssignedUsersCount,
    int ActiveUsersCount,
    int PermissionCount,
    IReadOnlyList<RolePermissionItemDto> Permissions,
    string Status,
    string UpdatedAt,
    string SegregationNote
);
