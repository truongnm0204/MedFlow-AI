namespace MedFlow.Application.DTOs.Services;

public record ServiceSearchResponseDto(
    IReadOnlyList<ExaminationServiceDto> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages);
