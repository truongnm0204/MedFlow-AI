using MedFlow.Application.DTOs.ExaminationRooms;

namespace MedFlow.Application.Interfaces.Services;

public interface IExaminationRoomService
{
    Task<ExaminationRoomListResponseDto> GetExaminationRoomListAsync(
        long actorUserId,
        GetExaminationRoomListRequest request,
        CancellationToken ct = default);

    Task<ExaminationRoomItemDto> CreateExaminationRoomAsync(
        long actorUserId,
        CreateExaminationRoomRequest request,
        CancellationToken ct = default);

    Task<ExaminationRoomItemDto> UpdateExaminationRoomAsync(
        long actorUserId,
        long id,
        UpdateExaminationRoomRequest request,
        CancellationToken ct = default);

    Task<ExaminationRoomDependencySummaryDto> CheckExaminationRoomDependenciesAsync(
        long? actorUserId,
        string idOrCode,
        CancellationToken ct = default);

    Task<DeactivateExaminationRoomResponseDto> DeactivateExaminationRoomAsync(
        long? actorUserId,
        string idOrCode,
        DeactivateExaminationRoomRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<DeactivateExaminationRoomResponseDto> ReactivateExaminationRoomAsync(
        long? actorUserId,
        string idOrCode,
        ReactivateExaminationRoomRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
