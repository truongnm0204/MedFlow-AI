using MedFlow.Application.DTOs.Diagnostic;

namespace MedFlow.Application.Interfaces.Services;

public interface IDiagnosticOrderReviewService
{
    Task<DiagnosticOrderReviewListResponseDto> GetDiagnosticOrdersAsync(
        long actorUserId,
        GetDiagnosticOrdersForReviewRequest? request = null,
        CancellationToken ct = default);

    Task<DiagnosticOrderReviewDetailResponseDto> GetDiagnosticOrderByIdOrCodeAsync(
        long actorUserId,
        string orderIdOrCode,
        CancellationToken ct = default);

    Task<ConfirmDiagnosticOrderResponseDto> ConfirmDiagnosticOrderAsync(
        long actorUserId,
        string orderIdOrCode,
        ConfirmDiagnosticOrderRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ConfirmDiagnosticOrderResponseDto> ConfirmDiagnosticOrdersBatchAsync(
        long actorUserId,
        ConfirmDiagnosticOrdersBatchRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
