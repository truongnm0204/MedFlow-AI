using MedFlow.Application.DTOs.Services;

namespace MedFlow.Application.Services.Services;

public interface IExaminationServiceCatalogService
{
    Task<ServiceSearchResponseDto> SearchAsync(
        SearchExaminationServicesRequest request,
        CancellationToken ct = default);

    Task<ExaminationServiceDto> GetByIdOrCodeAsync(
        string idOrCode,
        CancellationToken ct = default);
}
