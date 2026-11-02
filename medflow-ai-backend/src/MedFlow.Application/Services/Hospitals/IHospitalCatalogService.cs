using MedFlow.Application.DTOs.Hospitals;

namespace MedFlow.Application.Services.Hospitals;

public interface IHospitalCatalogService
{
    Task<IReadOnlyList<HospitalDto>> ListAsync(CancellationToken ct = default);
}
