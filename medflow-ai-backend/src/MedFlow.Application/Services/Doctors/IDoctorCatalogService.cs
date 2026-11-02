using MedFlow.Application.DTOs.Doctors;

namespace MedFlow.Application.Services.Doctors;

public interface IDoctorCatalogService
{
    Task<DoctorSearchResponseDto> SearchAsync(
        SearchDoctorsRequest request,
        CancellationToken ct = default);

    Task<DoctorDetailDto> GetByIdOrCodeAsync(
        string idOrCode,
        CancellationToken ct = default);
}
