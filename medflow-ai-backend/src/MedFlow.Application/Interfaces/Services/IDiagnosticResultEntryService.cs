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

    Task<EnterDiagnosticResultResponseDto> VerifyDiagnosticResultAsync(
        long actorUserId,
        string? orderIdOrCode,
        VerifyDiagnosticResultRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
        => throw new NotSupportedException("VerifyDiagnosticResultAsync is not implemented by this test double.");

    Task<DiagnosticReportFileDownloadDto> DownloadDiagnosticReportFileAsync(
        long actorUserId,
        string resultOrOrderIdOrCode,
        string? ipAddress = null,
        CancellationToken ct = default)
        => throw new NotSupportedException("DownloadDiagnosticReportFileAsync is not implemented by this test double.");
}
