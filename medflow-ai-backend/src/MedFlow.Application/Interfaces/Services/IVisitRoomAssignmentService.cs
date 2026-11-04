using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

public interface IVisitRoomAssignmentService
{
    Task<CompatibleExaminationRoomListResponseDto> GetCompatibleRoomsAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default);

    Task<AssignExaminationRoomResponseDto> AssignExaminationRoomAsync(
        long actorUserId,
        long visitId,
        AssignExaminationRoomRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<AssignExaminationRoomResponseDto> AssignExaminationRoomAsync(
        long actorUserId,
        string visitIdOrCode,
        AssignExaminationRoomRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
