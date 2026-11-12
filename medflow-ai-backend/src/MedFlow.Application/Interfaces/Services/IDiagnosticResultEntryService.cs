using MedFlow.Application.DTOs.Diagnostic;

namespace MedFlow.Application.Interfaces.Services;

public interface IDiagnosticResultEntryService
{
    Task<DiagnosticResultEntryListResponseDto> GetOrdersForResultEntryAsync(
        long actorUserId,
        GetDiagnosticOrdersForResultEntryRequest? request = null,
        CancellationToken ct = default);

    Task<DiagnosticResultEntryDetailResponseDto> GetOrderResultByIdOrCodeAsync(
        long actorUserId,
        string orderIdOrCode,
        CancellationToken ct = default);

    Task<DiagnosticResultViewListResponseDto> GetDiagnosticResultViewsAsync(
        long actorUserId,
        GetDiagnosticResultViewsRequest? request = null,
        CancellationToken ct = default);

    Task<DiagnosticResultViewDetailResponseDto> GetDiagnosticResultViewByIdOrCodeAsync(
        long actorUserId,
        string orderIdOrCode,
        CancellationToken ct = default);

    Task<EnterDiagnosticResultResponseDto> EnterDiagnosticResultAsync(
        long actorUserId,
        string? orderIdOrCode,
        EnterDiagnosticResultRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<EnterDiagnosticResultResponseDto> UpdateDiagnosticResultAsync(
        long actorUserId,
        string? orderIdOrCode,
        UpdateDiagnosticResultRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
