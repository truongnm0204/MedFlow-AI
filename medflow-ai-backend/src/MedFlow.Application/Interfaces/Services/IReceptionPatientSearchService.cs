using MedFlow.Application.DTOs.Reception;

namespace MedFlow.Application.Interfaces.Services;

public interface IReceptionPatientSearchService
{
    Task<SearchPatientsResponseDto> SearchPatientsAsync(
        SearchPatientsRequest request,
        CancellationToken ct = default,
        long? actorUserId = null);

    Task<ReceptionDashboardSummaryDto> GetDashboardSummaryAsync(
        long actorUserId,
        CancellationToken ct = default)
        => Task.FromResult(new ReceptionDashboardSummaryDto(0, 0, 0, 0));
}
