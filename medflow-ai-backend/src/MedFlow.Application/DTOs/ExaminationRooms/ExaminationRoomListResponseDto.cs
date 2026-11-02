namespace MedFlow.Application.DTOs.ExaminationRooms;

public sealed record ExaminationRoomListResponseDto(
    IReadOnlyList<ExaminationRoomItemDto> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    ExaminationRoomMetricsDto Metrics
);
