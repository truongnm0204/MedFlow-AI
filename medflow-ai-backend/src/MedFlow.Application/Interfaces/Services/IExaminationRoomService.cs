using MedFlow.Application.DTOs.ExaminationRooms;

namespace MedFlow.Application.Interfaces.Services;

public interface IExaminationRoomService
{
    Task<ExaminationRoomListResponseDto> GetExaminationRoomListAsync(
        GetExaminationRoomListRequest request,
        CancellationToken ct = default);

    Task<ExaminationRoomItemDto> CreateExaminationRoomAsync(
        CreateExaminationRoomRequest request,
        CancellationToken ct = default);

    Task<ExaminationRoomItemDto> UpdateExaminationRoomAsync(
        long id,
        UpdateExaminationRoomRequest request,
        CancellationToken ct = default);
}
