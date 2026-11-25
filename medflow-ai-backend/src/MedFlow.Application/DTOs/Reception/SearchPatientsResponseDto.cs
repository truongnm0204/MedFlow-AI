namespace MedFlow.Application.DTOs.Reception;

public sealed record ReceptionDashboardSummaryDto(
    int WaitingCheckIn,
    int InQueue,
    int InConsultation,
    int CompletedToday);

public sealed record SearchPatientsResponseDto(
    IReadOnlyList<PatientSearchResultItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    SearchTabCountsDto Counts,
    ReceptionDashboardSummaryDto? DashboardSummary = null);
