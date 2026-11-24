import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { fetchIntegrationConfigs, updateIntegrationConfig } from '../api/integration-config.api';
import type {
  IntegrationProviderKey,
  UpdateIntegrationConfigPayload,
} from '../types/integration-config.types';

export const ADMIN_INTEGRATIONS_QUERY_KEY = 'admin-integrations';

export function useIntegrationConfigs(hospitalId?: number | null, enabled = true) {
  return useQuery({
    queryKey: [ADMIN_INTEGRATIONS_QUERY_KEY, hospitalId ?? 'default'],
    queryFn: () => fetchIntegrationConfigs(hospitalId),
    enabled,
  });
}

export function useUpdateIntegrationConfig() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      providerKey,
      payload,
    }: {
      providerKey: IntegrationProviderKey;
      payload: UpdateIntegrationConfigPayload;
    }) => updateIntegrationConfig(providerKey, payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: [ADMIN_INTEGRATIONS_QUERY_KEY] });
      void qc.invalidateQueries({ queryKey: ['admin-audit-logs'] });
    },
  });
}
