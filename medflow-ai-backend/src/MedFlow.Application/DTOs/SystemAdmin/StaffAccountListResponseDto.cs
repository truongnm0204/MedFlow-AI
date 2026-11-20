namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record StaffAccountListResponseDto(
    IReadOnlyList<StaffAccountListItemDto> Data,
    IReadOnlyList<StaffAccountListItemDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    StaffAccountMetricsDto Metrics
);
