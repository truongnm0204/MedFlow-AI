using MedFlow.Application.DTOs.Payments;

namespace MedFlow.Application.Interfaces.Services;

public interface IDiagnosticOrderSelectionService
{
    Task<VisitChargesResponseDto> UpdateSelectionAsync(
        long actorUserId,
        string? visitIdOrCode,
        UpdateDiagnosticOrderSelectionRequestDto? request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<DeclineDiagnosticOrderResponseDto> DeclineOrderAsync(
        long actorUserId,
        string? visitIdOrCode,
        string? orderIdOrCode,
        DeclineDiagnosticOrderRequestDto? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
