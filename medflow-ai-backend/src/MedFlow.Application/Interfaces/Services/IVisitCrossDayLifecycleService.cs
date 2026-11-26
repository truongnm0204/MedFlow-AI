using MedFlow.Application.DTOs.Visits;

namespace MedFlow.Application.Interfaces.Services;

public interface IVisitCrossDayLifecycleService
{
    Task<VisitResumeContextResponseDto> GetResumeContextAsync(
        long actorUserId,
        string idOrCode,
        bool allowPatientRole = true,
        CancellationToken ct = default);

    Task<ResumeCrossDayVisitResponseDto> ResumeVisitAsync(
        long actorUserId,
        string idOrCode,
        ResumeCrossDayVisitRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<DeferVisitOrOrdersResponseDto> DeferVisitOrOrdersAsync(
        long actorUserId,
        string idOrCode,
        DeferVisitOrOrdersRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
