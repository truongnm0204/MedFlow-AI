using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

public interface IVisitRoomTransferService
{
    Task<VisitCompatibleRoomsResponseDto> GetCompatibleRoomsAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default);

    Task<VisitRoomTransferResponseDto> TransferToCompatibleRoomAsync(
        long actorUserId,
        string idOrCode,
        TransferVisitRoomRequest request,
        string? ipAddress,
        CancellationToken ct = default);
}
