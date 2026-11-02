using MedFlow.Application.DTOs.ExaminationSlots;

namespace MedFlow.Application.Interfaces.Services;

public interface IExaminationSlotAdminService
{
    Task<ExaminationSlotAdminListResponseDto> GetExaminationSlotListAsync(
        GetExaminationSlotListRequest request,
        CancellationToken ct = default);

    Task<ExaminationSlotAdminItemDto> CreateExaminationSlotAsync(
        CreateExaminationSlotRequest request,
        CancellationToken ct = default);

    Task<ExaminationSlotAdminItemDto> UpdateExaminationSlotAsync(
        long id,
        UpdateExaminationSlotRequest request,
        CancellationToken ct = default);
}

