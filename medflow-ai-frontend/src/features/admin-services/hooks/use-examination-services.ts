import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';

import {
  createExaminationServiceApi,
  deactivateExaminationServiceApi,
  fetchAdminServicePricesApi,
  fetchExaminationServicesApi,
  fetchServicePriceHistoryApi,
  reactivateExaminationServiceApi,
  updateExaminationServiceApi,
  updateServicePriceApi,
} from '../api/examination-service.api';
import type { ExaminationServiceFormValues } from '../schemas/examination-service.schema';
import { useExaminationServiceStore } from '../store/examination-service.store';
import type { UpdateServicePricePayload } from '../types/examination-service.types';

export const adminExaminationServicesQueryKey = ['admin', 'examination-services'] as const;
export const adminServicePricesQueryKey = ['admin', 'service-prices'] as const;

export function useAdminExaminationServices() {
  const query = useQuery({
    queryKey: adminExaminationServicesQueryKey,
    queryFn: fetchExaminationServicesApi,
  });

  useEffect(() => {
    if (query.data) {
      useExaminationServiceStore.setState({ services: query.data });
    }
  }, [query.data]);

  return query;
}

export function useAdminServicePrices() {
  const query = useQuery({
    queryKey: adminServicePricesQueryKey,
    queryFn: fetchAdminServicePricesApi,
  });

  useEffect(() => {
    if (query.data) {
      useExaminationServiceStore.setState({ services: query.data });
    }
  }, [query.data]);

  return query;
}

export function useServicePriceHistory(idOrCode: string | null) {
  return useQuery({
    queryKey: ['admin', 'service-prices', 'history', idOrCode ?? ''] as const,
    queryFn: () => fetchServicePriceHistoryApi(idOrCode!),
    enabled: Boolean(idOrCode && idOrCode.trim().length > 0),
  });
}

export function useCreateExaminationServiceMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createExaminationServiceApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminExaminationServicesQueryKey });
      void queryClient.invalidateQueries({ queryKey: adminServicePricesQueryKey });
    },
  });
}

export function useUpdateExaminationServiceMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      serviceId,
      values,
    }: {
      serviceId: string;
      values: Partial<ExaminationServiceFormValues> & { price?: number };
    }) => updateExaminationServiceApi(serviceId, values),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminExaminationServicesQueryKey });
      void queryClient.invalidateQueries({ queryKey: adminServicePricesQueryKey });
    },
  });
}

export function useDeactivateExaminationServiceMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ idOrCode, reason }: { idOrCode: string; reason?: string }) =>
      deactivateExaminationServiceApi(idOrCode, reason),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminExaminationServicesQueryKey });
      void queryClient.invalidateQueries({ queryKey: adminServicePricesQueryKey });
    },
  });
}

export function useReactivateExaminationServiceMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: reactivateExaminationServiceApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminExaminationServicesQueryKey });
      void queryClient.invalidateQueries({ queryKey: adminServicePricesQueryKey });
    },
  });
}

export function useUpdateServicePriceMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      serviceId,
      payload,
    }: {
      serviceId: string;
      payload: UpdateServicePricePayload;
    }) => updateServicePriceApi(serviceId, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminServicePricesQueryKey });
      void queryClient.invalidateQueries({ queryKey: adminExaminationServicesQueryKey });
    },
  });
}
