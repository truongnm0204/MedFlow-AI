namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record SystemAdminMetricsDto(
    int TotalUsers,
    int ActiveUsers,
    int LockedOrInactiveUsers,
    int TotalStaffAccounts,
    int ActiveStaffAccounts,
    int InactiveOrLockedStaffAccounts,
    int TotalRoles,
    int TotalPermissions,
    int TotalHospitals,
    int ActiveDepartments,
    int TotalAuditLogs,
    int RecentAuditCount24h
);
