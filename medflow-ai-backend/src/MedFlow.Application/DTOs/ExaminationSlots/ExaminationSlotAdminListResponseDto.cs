using System.Text.Json.Serialization;

namespace MedFlow.Application.DTOs.ExaminationSlots;

public sealed record ExaminationSlotAdminListResponseDto(
    IReadOnlyList<ExaminationSlotAdminItemDto> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    ExaminationSlotMetricsDto Metrics
)
{
    [JsonPropertyName("items")]
    public IReadOnlyList<ExaminationSlotAdminItemDto> Items => Data;
}

