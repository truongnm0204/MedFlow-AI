namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record SystemAdminDashboardResponseDto(
    SystemAdminProfileDto AdminProfile,
    SystemAdminMetricsDto Metrics,
    IReadOnlyList<SystemAdminAllowedModuleDto> AllowedModules,
    IReadOnlyList<SystemAdminRoleSummaryDto> RoleSummaries,
    IReadOnlyList<SystemAdminAuditLogItemDto> RecentAuditLogs,
    DateTime GeneratedAtUtc
);
