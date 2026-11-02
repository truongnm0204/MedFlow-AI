using MedFlow.Application.DTOs.ExaminationSlots;

namespace MedFlow.Application.Interfaces.Services;

public interface IExaminationSlotAdminService
{
    Task<ExaminationSlotAdminListResponseDto> GetExaminationSlotListAsync(
        GetExaminationSlotListRequest request,
        CancellationToken ct = default);
}
