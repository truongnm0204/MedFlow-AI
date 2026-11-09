using MedFlow.Application.DTOs.Reception;

namespace MedFlow.Application.Interfaces.Services;

public interface IReceptionPatientSearchService
{
    Task<SearchPatientsResponseDto> SearchPatientsAsync(SearchPatientsRequest request, CancellationToken ct = default);
}
