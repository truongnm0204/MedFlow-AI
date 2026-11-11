using MedFlow.Application.DTOs.Diagnostic;

namespace MedFlow.Application.Interfaces.Services;

public interface IDiagnosticQueueService
{
    Task<DiagnosticQueueBoardResponseDto> GetDiagnosticQueueAsync(
        long actorUserId,
        GetDiagnosticQueueRequest? request = null,
        CancellationToken ct = default);

    Task<DiagnosticQueueEntryDetailResponseDto> GetDiagnosticQueueEntryByIdOrCodeAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default);

    Task<CallNextDiagnosticPatientResponseDto> CallNextDiagnosticPatientAsync(
        long actorUserId,
        string servicePointId,
        CallNextDiagnosticPatientRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ManageDiagnosticQueueActionResponseDto> UpdateDiagnosticQueueEntryStatusAsync(
        long actorUserId,
        string idOrCode,
        UpdateDiagnosticQueueStatusRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ManageDiagnosticQueueActionResponseDto> MarkDiagnosticMissedTurnAsync(
        long actorUserId,
        string idOrCode,
        DiagnosticQueueTurnActionRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ManageDiagnosticQueueActionResponseDto> RestoreDiagnosticSkippedTurnAsync(
        long actorUserId,
        string idOrCode,
        DiagnosticQueueTurnActionRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ManageDiagnosticQueueActionResponseDto> CancelDiagnosticQueueEntryAsync(
        long actorUserId,
        string idOrCode,
        CancelDiagnosticQueueEntryRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
