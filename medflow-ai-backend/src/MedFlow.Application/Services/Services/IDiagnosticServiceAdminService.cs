using MedFlow.Application.DTOs.Services;

namespace MedFlow.Application.Services.Services;

public interface IDiagnosticServiceAdminService
{
    Task<DiagnosticServiceAdminListResponseDto> GetDiagnosticServiceListAsync(
        long actorUserId,
        GetDiagnosticServiceListAdminRequest request,
        CancellationToken ct = default);

    Task<DiagnosticServiceFormOptionsDto> GetFormOptionsAsync(
        long actorUserId,
        CancellationToken ct = default);

    Task<DiagnosticServiceAdminItemDto> CreateDiagnosticServiceAsync(
        long actorUserId,
        CreateDiagnosticServiceAdminRequest? request,
        string? ipAddress,
        CancellationToken ct = default);

    Task<DiagnosticServiceAdminItemDto> UpdateDiagnosticServiceAsync(
        long actorUserId,
        long serviceId,
        UpdateDiagnosticServiceAdminRequest? request,
        string? ipAddress,
        CancellationToken ct = default);

    Task<DiagnosticServiceAdminItemDto> DeactivateDiagnosticServiceAsync(
        long actorUserId,
        long serviceId,
        DeactivateDiagnosticServiceAdminRequest? request,
        string? ipAddress,
        CancellationToken ct = default);
}
