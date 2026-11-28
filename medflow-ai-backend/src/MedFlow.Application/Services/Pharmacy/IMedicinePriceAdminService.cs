using MedFlow.Application.DTOs.Pharmacy;

namespace MedFlow.Application.Services.Pharmacy;

public interface IMedicinePriceAdminService
{
    Task<MedicinePriceAdminListResponseDto> GetMedicinePriceListAsync(
        long actorUserId,
        GetMedicinePriceListAdminRequest request,
        CancellationToken ct = default);

    Task<MedicinePriceDetailResponseDto> GetMedicinePriceDetailAsync(
        long actorUserId,
        string idOrCode,
        long? hospitalId = null,
        CancellationToken ct = default);

    Task<MedicinePriceHistoryResponseDto> GetMedicinePriceHistoryAsync(
        long actorUserId,
        string idOrCode,
        long? hospitalId = null,
        CancellationToken ct = default);

    Task<MedicinePriceAdminItemDto> UpdateMedicinePriceAsync(
        long actorUserId,
        string? routeIdOrCode,
        UpdateMedicinePriceAdminRequest? request,
        string? ipAddress = null,
        CancellationToken ct = default);
}
