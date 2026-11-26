using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

public interface IVisitQueueEnrollmentService
{
    Task<VisitQueueEnrollmentContextDto> GetEnrollmentContextAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default);

    Task<VisitQueueEnrollmentTicketDto> EnrollAsync(
        long actorUserId,
        string visitIdOrCode,
        CreateVisitQueueEnrollmentRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<LeaveQueueEntryResponseDto> LeaveQueueAsync(
        long actorUserId,
        string queueEntryIdOrCode,
        LeaveQueueEntryRequestDto? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<VisitQueueEnrollmentTicketDto> CallPharmacyQueueEntryAsync(
        long actorUserId,
        string queueEntryIdOrCode,
        PharmacyQueueActionRequestDto? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<VisitQueueEnrollmentTicketDto> UpdatePharmacyQueueEntryStatusAsync(
        long actorUserId,
        string queueEntryIdOrCode,
        PharmacyQueueActionRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
