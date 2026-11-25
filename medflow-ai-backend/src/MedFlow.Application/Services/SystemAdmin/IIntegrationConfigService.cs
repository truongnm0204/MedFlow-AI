using MedFlow.Application.DTOs.SystemAdmin;

namespace MedFlow.Application.Services.SystemAdmin;

public interface IIntegrationConfigService
{
    Task<IntegrationConfigListResponseDto> GetIntegrationsAsync(
        long actorUserId,
        GetIntegrationsRequest request,
        CancellationToken ct = default);

    Task<IntegrationProviderItemDto> UpdateIntegrationAsync(
        long actorUserId,
        string providerKey,
        UpdateIntegrationConfigRequest request,
        string? ipAddress,
        CancellationToken ct = default);
}
