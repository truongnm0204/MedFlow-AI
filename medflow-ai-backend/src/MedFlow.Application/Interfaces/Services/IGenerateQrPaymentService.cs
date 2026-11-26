using MedFlow.Application.DTOs.Payments;

namespace MedFlow.Application.Interfaces.Services;

public interface IGenerateQrPaymentService
{
    Task<GenerateQrPaymentResponseDto> CreateAsync(
        long actorUserId,
        string? idOrCode,
        string? ipAddress,
        CancellationToken ct = default);

    Task<GenerateQrPaymentResponseDto> CreateAsync(
        long actorUserId,
        string? idOrCode,
        string? ipAddress,
        GenerateQrPaymentRequestDto? request,
        CancellationToken ct = default)
        => CreateAsync(actorUserId, idOrCode, ipAddress, ct);
}
