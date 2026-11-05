namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record SystemAdminRoleSummaryDto(
    long RoleId,
    string Code,
    string Name,
    int AssignedUserCount,
    int ActiveUserCount,
    int PermissionCount,
    IReadOnlyList<string> Permissions
);
