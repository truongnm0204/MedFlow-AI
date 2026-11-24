import { useEffect } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  createExaminationServiceApi,
  deactivateExaminationServiceApi,
  fetchExaminationServicesApi,
  reactivateExaminationServiceApi,
  updateExaminationServiceApi,
  updateServicePriceApi,
} from '../api/examination-service.api';
import type { ExaminationServiceFormValues } from '../schemas/examination-service.schema';
import { useExaminationServiceStore } from '../store/examination-service.store';
import type { UpdateServicePricePayload } from '../types/examination-service.types';

export const adminExaminationServicesQueryKey = ['admin', 'examination-services'] as const;

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

export function useCreateExaminationServiceMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createExaminationServiceApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminExaminationServicesQueryKey });
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
    },
  });
}

export function useReactivateExaminationServiceMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: reactivateExaminationServiceApi,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminExaminationServicesQueryKey });
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
      void queryClient.invalidateQueries({ queryKey: adminExaminationServicesQueryKey });
    },
  });
}
