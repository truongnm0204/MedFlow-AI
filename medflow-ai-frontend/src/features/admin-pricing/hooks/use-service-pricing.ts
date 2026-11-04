import { useMutation, useQueryClient, useQuery } from '@tanstack/react-query';

import { fetchServicePricing, updateServicePrice } from '../api/service-pricing.api';
import type {
  ServicePricingFilter,
  ServicePricingItem,
  ServicePricingResponse,
  UpdateServicePricePayload,
} from '../types/service-pricing.types';

export function useServicePricing(
  filters: Partial<ServicePricingFilter> = {},
  page = 1,
  pageSize = 10,
) {
  return useQuery<ServicePricingResponse, Error>({
    queryKey: ['admin-service-pricing', filters, page, pageSize],
    queryFn: () => fetchServicePricing(filters, page, pageSize),
    placeholderData: (previousData) => previousData,
    staleTime: 60_000,
    retry: false,
  });
}

export function useUpdateServicePrice() {
  const queryClient = useQueryClient();
  return useMutation<ServicePricingItem, Error, { id: string; payload: UpdateServicePricePayload }>(
    {
      mutationFn: ({ id, payload }) => updateServicePrice(id, payload),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['admin-service-pricing'] });
      },
    },
  );
}
