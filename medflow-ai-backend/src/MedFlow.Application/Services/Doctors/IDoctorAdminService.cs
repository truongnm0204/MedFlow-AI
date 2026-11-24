using MedFlow.Application.DTOs.Doctors;

namespace MedFlow.Application.Services.Doctors;

public interface IDoctorAdminService
{
    Task<DoctorAdminListResponseDto> GetDoctorListAsync(
        long actorUserId,
        GetDoctorListAdminRequest request,
        CancellationToken ct = default);

    Task<DoctorAdminListItemDto> CreateDoctorAsync(
        long actorUserId,
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

    Task<DeactivateDoctorResponseDto> DeactivateDoctorAsync(
        long? actorUserId,
        string idOrCode,
        DeactivateDoctorRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<DeactivateDoctorResponseDto> ReactivateDoctorAsync(
        long? actorUserId,
        string idOrCode,
        ReactivateDoctorRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
