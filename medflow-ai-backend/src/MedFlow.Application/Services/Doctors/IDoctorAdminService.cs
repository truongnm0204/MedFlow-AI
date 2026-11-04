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

    Task<DoctorAdminListItemDto> UpdateDoctorAsync(
        long actorUserId,
        string idOrCode,
        UpdateDoctorAdminRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<DoctorAdminListItemDto> UpdateDoctorAsync(
        long actorUserId,
        long id,
        UpdateDoctorAdminRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);
}

