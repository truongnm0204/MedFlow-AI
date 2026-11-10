using MedFlow.Application.DTOs.Consultation;

namespace MedFlow.Application.Interfaces.Services;

public interface IConsultationWorkspaceService
{
    Task<ConsultationWorkspaceResponseDto> GetWorkspaceAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default);

    Task<ConsultationWorkspaceResponseDto> OpenWorkspaceAsync(
        long actorUserId,
        string visitIdOrCode,
        OpenConsultationWorkspaceRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<ConsultationWorkspaceResponseDto> RecordClinicalExaminationAsync(
        long actorUserId,
        string visitIdOrCode,
        RecordClinicalExaminationRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<DiagnosticOrderCatalogResponseDto> GetDiagnosticOrderCatalogAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default);

    Task<ConsultationWorkspaceResponseDto> CreateDiagnosticOrderAsync(
        long actorUserId,
        string visitIdOrCode,
        CreateDiagnosticOrderRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
