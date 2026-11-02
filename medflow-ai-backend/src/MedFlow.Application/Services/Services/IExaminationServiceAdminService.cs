using MedFlow.Application.DTOs.Services;

namespace MedFlow.Application.Services.Services;

public interface IExaminationServiceAdminService
{
    Task<ExaminationServiceAdminListResponseDto> GetExaminationServiceListAsync(
        GetExaminationServiceListAdminRequest request,
        CancellationToken ct = default);

    Task<ExaminationServiceAdminItemDto> CreateExaminationServiceAsync(
        CreateExaminationServiceRequest request,
        CancellationToken ct = default);
}

