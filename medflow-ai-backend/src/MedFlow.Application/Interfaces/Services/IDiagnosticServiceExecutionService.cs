using MedFlow.Application.DTOs.Diagnostic;

namespace MedFlow.Application.Interfaces.Services;

public interface IDiagnosticServiceExecutionService
{
    Task<DiagnosticServiceExecutionListResponseDto> GetExecutionsAsync(
        long actorUserId,
        GetDiagnosticServiceExecutionsRequest? request = null,
        CancellationToken ct = default);

    Task<DiagnosticServiceExecutionDetailResponseDto> GetExecutionByIdOrCodeAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default);

    Task<UpdateDiagnosticServiceStatusResponseDto> UpdateServiceExecutionStatusAsync(
        long actorUserId,
        string idOrCode,
        UpdateDiagnosticServiceStatusRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<UpdateDiagnosticServiceStatusResponseDto> StartServiceExecutionAsync(
        long actorUserId,
        string idOrCode,
        UpdateDiagnosticServiceStatusRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<UpdateDiagnosticServiceStatusResponseDto> CompleteServiceExecutionAsync(
        long actorUserId,
        string idOrCode,
        UpdateDiagnosticServiceStatusRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<UpdateDiagnosticServiceStatusResponseDto> UpdateServiceExecutionStatusBatchAsync(
        long actorUserId,
        UpdateDiagnosticServiceStatusBatchRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
