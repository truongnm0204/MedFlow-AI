namespace MedFlow.Application.DTOs.Doctors;

public sealed record DoctorAdminListResponseDto(
    IReadOnlyList<DoctorAdminListItemDto> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages
);

