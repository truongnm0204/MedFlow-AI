using MedFlow.Application.DTOs.ExaminationRooms;

namespace MedFlow.Application.Interfaces.Services;

public interface IExaminationRoomService
{
    Task<ExaminationRoomListResponseDto> GetExaminationRoomListAsync(
        GetExaminationRoomListRequest request,
        CancellationToken ct = default);
}
