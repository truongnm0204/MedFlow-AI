import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';

import {
  fetchAdminMedicinePricesApi,
  fetchMedicinePriceHistoryApi,
  updateMedicinePriceApi,
} from '../api/medicine-price.api';
import { useMedicinePriceStore } from '../store/medicine-price.store';
import type {
  MedicinePriceListParams,
  UpdateMedicinePricePayload,
} from '../types/medicine-price.types';

export const adminMedicinePricesQueryKey = ['admin', 'medicine-prices'] as const;

export function useAdminMedicinePrices(params?: MedicinePriceListParams) {
  const query = useQuery({
    queryKey: [...adminMedicinePricesQueryKey, params ?? {}] as const,
    queryFn: () => fetchAdminMedicinePricesApi(params),
  });

  useEffect(() => {
    if (query.data) {
      useMedicinePriceStore.setState({ items: query.data.data });
    }
  }, [query.data]);

  return query;
}

export function useMedicinePriceHistory(idOrCode: string | null, hospitalId?: number) {
  return useQuery({
    queryKey: [
      ...adminMedicinePricesQueryKey,
      'history',
      idOrCode ?? '',
      hospitalId ?? '',
    ] as const,
    queryFn: () => fetchMedicinePriceHistoryApi(idOrCode!, hospitalId),
    enabled: Boolean(idOrCode && idOrCode.trim().length > 0),
  });
}

export function useUpdateMedicinePriceMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload: UpdateMedicinePricePayload;
    }) => updateMedicinePriceApi(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminMedicinePricesQueryKey });
    },
  });
}
