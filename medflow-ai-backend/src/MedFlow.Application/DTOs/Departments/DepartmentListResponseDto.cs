namespace MedFlow.Application.DTOs.Departments;

public sealed record DepartmentListResponseDto(
    IReadOnlyList<DepartmentItemDto> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    DepartmentMetricsDto Metrics
);

