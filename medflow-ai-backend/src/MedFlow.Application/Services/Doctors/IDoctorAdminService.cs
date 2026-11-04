using MedFlow.Application.DTOs.Doctors;

namespace MedFlow.Application.Services.Doctors;

public interface IDoctorAdminService
{
    Task<DoctorAdminListResponseDto> GetDoctorListAsync(
        GetDoctorListAdminRequest request,
        CancellationToken ct = default);

    Task<DoctorAdminListItemDto> CreateDoctorAsync(
        CreateDoctorRequest request,
        CancellationToken ct = default);
}

