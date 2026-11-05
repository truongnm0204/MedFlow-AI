namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record RoleListResponseDto(
    IReadOnlyList<RoleListItemDto> Data,
    IReadOnlyList<RoleListItemDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    RoleMetricsDto Metrics
);
