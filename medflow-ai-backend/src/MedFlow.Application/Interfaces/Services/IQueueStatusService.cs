using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

public interface IQueueStatusService
{
    Task<QueueEntryStatusDetailDto> GetQueueEntryStatusAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default);

    Task<UpdateQueueStatusResponseDto> UpdateQueueEntryStatusAsync(
        long actorUserId,
        string idOrCode,
        UpdateQueueStatusRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
