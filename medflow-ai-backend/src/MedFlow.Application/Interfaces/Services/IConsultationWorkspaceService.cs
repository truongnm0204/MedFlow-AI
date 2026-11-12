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

    Task<ConsultationWorkspaceResponseDto> CreateAdditionalDiagnosticOrderAsync(
        long actorUserId,
        string visitIdOrCode,
        CreateAdditionalDiagnosticOrderRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<DiagnosticResultsReviewResponseDto> GetDiagnosticResultsAsync(
        long actorUserId,
        string visitIdOrCode,
        GetDiagnosticResultsRequest? request = null,
        CancellationToken ct = default);

    Task<ConsultationDiagnosticResultItemDto> GetDiagnosticResultByOrderAsync(
        long actorUserId,
        string visitIdOrCode,
        string orderIdOrCode,
        CancellationToken ct = default);


    Task<DiagnosticResultsTrackingResponseDto> GetDiagnosticResultsTrackingAsync(
        long actorUserId,
        GetDiagnosticResultsTrackingRequest? request = null,
        CancellationToken ct = default);
    Task<ConsultationWorkspaceResponseDto> ReviewDiagnosticResultsAsync(
        long actorUserId,
        string visitIdOrCode,
        ReviewDiagnosticResultsRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
    Task<ConsultationWorkspaceResponseDto> RecordDiagnosisAsync(
        long actorUserId,
        string visitIdOrCode,
        RecordDiagnosisRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<Icd10CatalogItemDto>> SearchIcd10CodesAsync(
        long actorUserId,
        string? query = null,
        bool primaryOnly = false,
        int limit = 50,
        CancellationToken ct = default);

    Task<ConsultationWorkspaceResponseDto> CompleteVisitAsync(
        long actorUserId,
        string visitIdOrCode,
        CompleteVisitRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
