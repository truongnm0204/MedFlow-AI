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

    Task<CloseExaminationSlotResponseDto> CloseExaminationSlotAsync(
        long? actorUserId,
        string idOrCode,
        CloseExaminationSlotRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<BatchCloseExaminationSlotsResponseDto> BatchCloseExaminationSlotsAsync(
        long? actorUserId,
        BatchCloseExaminationSlotsRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<CloseExaminationSlotResponseDto> ReopenExaminationSlotAsync(
        long? actorUserId,
        string idOrCode,
        ReopenExaminationSlotRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}

