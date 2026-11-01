using MedFlow.Application.DTOs.Doctors;

namespace MedFlow.Application.Services.Doctors;

public interface IDoctorCatalogService
{
    Task<DoctorDetailDto> GetByIdOrCodeAsync(
        string idOrCode,
        CancellationToken ct = default);
}
