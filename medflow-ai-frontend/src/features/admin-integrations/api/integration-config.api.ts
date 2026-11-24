import { apiClient } from '@/shared/lib/axios';

import type {
  IntegrationConfigListResponseDto,
  IntegrationProviderItemDto,
  IntegrationProviderKey,
  UpdateIntegrationConfigPayload,
} from '../types/integration-config.types';

export async function fetchIntegrationConfigs(
  hospitalId?: number | null,
): Promise<IntegrationConfigListResponseDto> {
  const params: Record<string, number> = {};
  if (typeof hospitalId === 'number' && hospitalId > 0) {
    params.hospitalId = hospitalId;
  }

  const { data } = await apiClient.get<IntegrationConfigListResponseDto>('/admin/integrations', {
    params,
  });
  return data;
}

export async function updateIntegrationConfig(
  providerKey: IntegrationProviderKey,
  payload: UpdateIntegrationConfigPayload,
): Promise<IntegrationProviderItemDto> {
  const slug = providerKey.toLowerCase().replaceAll('_', '-');
  const { data } = await apiClient.put<IntegrationProviderItemDto>(
    `/admin/integrations/${slug}`,
    payload,
  );
  return data;
}
