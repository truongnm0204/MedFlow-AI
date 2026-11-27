using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

public interface IJourneySlipService
{
    Task<JourneySlipDto> GetJourneySlipAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default);

    Task<JourneySlipPrintResultDto> PrintJourneySlipAsync(
        long actorUserId,
        string visitIdOrCode,
        string? ipAddress = null,
        CancellationToken ct = default);
}
