namespace MedFlow.Application.DTOs.Reception;

public sealed record SearchPatientsResponseDto(
    IReadOnlyList<PatientSearchResultItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    SearchTabCountsDto Counts);
