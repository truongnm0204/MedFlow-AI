namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record RolePermissionsCatalogResponseDto(
    IReadOnlyList<RoleListItemDto> Roles,
    IReadOnlyList<RolePermissionItemDto> Permissions,
    RoleMetricsDto Metrics
);
