namespace MedFlow.Application.DTOs.Doctors;

public sealed record DoctorSearchResponseDto(
    IReadOnlyList<DoctorSearchItemDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages);
