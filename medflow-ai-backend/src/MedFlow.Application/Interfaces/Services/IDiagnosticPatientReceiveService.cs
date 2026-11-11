using MedFlow.Application.DTOs.Diagnostic;

namespace MedFlow.Application.Interfaces.Services;

public interface IDiagnosticPatientReceiveService
{
    Task<DiagnosticPatientReceiveListResponseDto> GetOrdersForReceiveAsync(
        long actorUserId,
        GetDiagnosticOrdersForReceiveRequest? request = null,
        CancellationToken ct = default);

    Task<DiagnosticPatientReceiveDetailResponseDto> GetOrderForReceiveByIdOrCodeAsync(
        long actorUserId,
        string orderIdOrCode,
        CancellationToken ct = default);

    Task<ReceiveDiagnosticPatientResponseDto> ReceiveDiagnosticPatientAsync(
        long actorUserId,
        string orderIdOrCode,
        ReceiveDiagnosticPatientRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ReceiveDiagnosticPatientResponseDto> ReceiveDiagnosticPatientsBatchAsync(
        long actorUserId,
        ReceiveDiagnosticPatientsBatchRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
