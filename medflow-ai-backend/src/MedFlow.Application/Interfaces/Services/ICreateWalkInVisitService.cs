using MedFlow.Application.DTOs.Reception;

namespace MedFlow.Application.Interfaces.Services;

public interface ICreateWalkInVisitService
{
    Task<CreateWalkInVisitResponseDto> CreateWalkInVisitAsync(
        long actorUserId,
        CreateWalkInVisitRequest request,
        string? routePatientIdOrCode = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<CreateWalkInVisitResponseDto> GetWalkInVisitByIdOrCodeAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default);

    Task<WalkInCatalogResponseDto> GetWalkInCatalogAsync(
        long actorUserId,
        CancellationToken ct = default);
}
