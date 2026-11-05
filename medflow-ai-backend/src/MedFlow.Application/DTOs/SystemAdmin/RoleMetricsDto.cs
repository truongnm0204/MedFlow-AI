namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record RoleMetricsDto(
    int TotalRoles,
    int ActiveRoles,
    int InactiveRoles,
    int SystemRoles,
    int CustomRoles,
    int TotalAssignedAccounts,
    int ActiveAssignedAccounts,
    int TotalPermissions
);
