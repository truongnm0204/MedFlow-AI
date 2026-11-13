using MedFlow.Application.DTOs.Payments;

namespace MedFlow.Application.Interfaces.Services;

public interface IVisitChargesService
{
    Task<VisitChargesResponseDto> GetAsync(
        long actorUserId,
        string? idOrCode,
        CancellationToken ct = default);
}
