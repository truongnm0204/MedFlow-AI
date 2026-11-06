using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

public interface IExaminationQueueService
{
    Task<ExaminationQueueResponseDto> GetExaminationQueueAsync(
        long actorUserId,
        GetExaminationQueueRequest? request = null,
        CancellationToken ct = default);

    Task<EarlyArrivalBackfillResponseDto> ApplyEarlyArrivalBackfillAsync(
        long actorUserId,
        string idOrCode,
        EarlyArrivalBackfillRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
