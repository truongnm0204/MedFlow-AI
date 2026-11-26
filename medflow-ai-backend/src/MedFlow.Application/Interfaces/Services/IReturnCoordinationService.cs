using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

public interface IReturnCoordinationService
{
    Task<ReturnCoordinationContextDto> GetReturnCoordinationContextAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default);

    Task<ReturnEnrollmentResponseDto> EnrollReturnConsultationAsync(
        long actorUserId,
        string visitIdOrCode,
        CreateReturnEnrollmentRequestDto? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
