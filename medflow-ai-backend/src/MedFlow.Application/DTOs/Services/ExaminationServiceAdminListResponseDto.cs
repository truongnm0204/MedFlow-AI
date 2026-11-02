namespace MedFlow.Application.DTOs.Services;

public sealed record ExaminationServiceAdminListResponseDto(
    IReadOnlyList<ExaminationServiceAdminItemDto> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    ExaminationServiceAdminMetricsDto Metrics
);
