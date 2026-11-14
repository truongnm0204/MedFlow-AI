using MedFlow.Application.DTOs.Payments;

namespace MedFlow.Application.Interfaces.Services;

public interface IGenerateQrPaymentService
{
    Task<GenerateQrPaymentResponseDto> CreateAsync(
        long actorUserId,
        string? idOrCode,
        string? ipAddress,
        CancellationToken ct = default);
}
