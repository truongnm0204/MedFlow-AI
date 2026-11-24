using MedFlow.Application.DTOs.Services;

namespace MedFlow.Application.Services.Services;

public interface IServicePriceAdminService
{
    Task<ServicePriceAdminListResponseDto> GetServicePriceListAsync(
        long actorUserId,
        GetServicePriceListAdminRequest request,
        CancellationToken ct = default);

    Task<ServicePriceDetailResponseDto> GetServicePriceDetailAsync(
        long actorUserId,
        string idOrCode,
        long? hospitalId = null,
        CancellationToken ct = default);

    Task<ServicePriceAdminItemDto> UpdateServicePriceAsync(
        long actorUserId,
        string? routeIdOrCode,
        UpdateServicePriceAdminRequest? request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
