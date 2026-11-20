using MedFlow.Application.DTOs.Pharmacy;

namespace MedFlow.Application.Interfaces.Services;

public interface IDispensingResultService
{
    Task<DispensingResultDetailDto> GetDispensingResultAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null);
}
