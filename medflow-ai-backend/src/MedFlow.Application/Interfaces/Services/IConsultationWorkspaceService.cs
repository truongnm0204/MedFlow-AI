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
}
