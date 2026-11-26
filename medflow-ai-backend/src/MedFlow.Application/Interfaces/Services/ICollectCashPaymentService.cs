using MedFlow.Application.DTOs.Payments;

namespace MedFlow.Application.Interfaces.Services;

public interface ICollectCashPaymentService
{
    Task<CollectCashPaymentResponseDto> CollectAsync(
        long actorUserId,
        string? idOrCode,
        CollectCashPaymentRequestDto? request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
