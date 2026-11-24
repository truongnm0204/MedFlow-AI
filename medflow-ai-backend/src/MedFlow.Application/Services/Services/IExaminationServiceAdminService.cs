using MedFlow.Application.DTOs.Services;

namespace MedFlow.Application.Services.Services;

public interface IExaminationServiceAdminService
{
    Task<ExaminationServiceAdminListResponseDto> GetExaminationServiceListAsync(
        long actorUserId,
        GetExaminationServiceListAdminRequest request,
        CancellationToken ct = default);

    Task<ExaminationServiceAdminItemDto> CreateExaminationServiceAsync(
        long actorUserId,
        CreateExaminationServiceRequest request,
        CancellationToken ct = default);

    Task<ExaminationServiceAdminItemDto> UpdateExaminationServiceAsync(
        long actorUserId,
        long id,
        UpdateExaminationServiceRequest request,
        CancellationToken ct = default);

    Task<DeactivateExaminationServiceResponseDto> DeactivateExaminationServiceAsync(
        long? actorUserId,
        string idOrCode,
        DeactivateExaminationServiceRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<DeactivateExaminationServiceResponseDto> ReactivateExaminationServiceAsync(
        long? actorUserId,
        string idOrCode,
        ReactivateExaminationServiceRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
