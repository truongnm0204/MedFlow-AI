using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

public interface IExaminationQueueService
{
    Task<ExaminationQueueResponseDto> GetExaminationQueueAsync(
        long actorUserId,
        GetExaminationQueueRequest? request = null,
        CancellationToken ct = default);

    Task<DoctorAssignedQueueResponseDto> GetDoctorAssignedQueueAsync(
        long actorUserId,
        GetDoctorAssignedQueueRequest? request = null,
        CancellationToken ct = default);

    Task<EarlyArrivalBackfillResponseDto> ApplyEarlyArrivalBackfillAsync(
        long actorUserId,
        string idOrCode,
        EarlyArrivalBackfillRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<QueueTurnActionResponseDto> MarkMissedTurnAsync(
        long actorUserId,
        string idOrCode,
        MarkMissedTurnRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<QueueTurnActionResponseDto> RestoreSkippedTurnAsync(
        long actorUserId,
        string idOrCode,
        RestoreQueueTurnRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<CallNextPatientResponseDto> CallNextPatientAsync(
        long actorUserId,
        string servicePointId,
        CallNextPatientRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<CreateReturnToDoctorQueueEntryResponseDto> CreateReturnToDoctorQueueEntryAsync(
        long actorUserId,
        string idOrCode,
        CreateReturnToDoctorQueueEntryRequest request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
