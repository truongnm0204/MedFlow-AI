using MedFlow.Application.DTOs.Diagnostic;

namespace MedFlow.Application.Interfaces.Services;

public interface IDiagnosticOrderRoutingService
{
    Task<ReassignDiagnosticOrderResponseDto> ReassignAsync(
        long actorUserId,
        string orderIdOrCode,
        ReassignDiagnosticOrderRequest? request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<DiagnosticOrderAssignmentSlipDto> GetVisitAssignmentsAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default);
}
