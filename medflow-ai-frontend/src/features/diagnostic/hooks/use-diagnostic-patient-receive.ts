import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  fetchDiagnosticOrdersForReceive,
  fetchDiagnosticPatientReceiveDetail,
  receiveDiagnosticPatient,
  receiveDiagnosticPatientsBatch,
} from '../api/diagnostic.api';
import type {
  GetDiagnosticOrdersForReceiveParams,
  ReceiveDiagnosticPatientRequest,
  ReceiveDiagnosticPatientsBatchRequest,
} from '../types/diagnostic-patient-receive.types';

export function useDiagnosticOrdersForReceive(
  params?: GetDiagnosticOrdersForReceiveParams,
  enabled = true,
) {
  return useQuery({
    queryKey: ['diagnostic', 'orders', 'receive', params ?? {}],
    queryFn: () => fetchDiagnosticOrdersForReceive(params),
    enabled,
    staleTime: 30_000,
  });
}

export function useDiagnosticPatientReceiveDetail(idOrCode?: string | null, enabled = true) {
  const normalized = idOrCode?.trim() ?? '';
  return useQuery({
    queryKey: ['diagnostic', 'orders', 'receive', 'detail', normalized],
    queryFn: () => fetchDiagnosticPatientReceiveDetail(normalized),
    enabled: enabled && Boolean(normalized),
    staleTime: 30_000,
  });
}

export function useReceiveDiagnosticPatient() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      idOrCode,
      payload,
    }: {
      idOrCode: string;
      payload?: ReceiveDiagnosticPatientRequest;
    }) => receiveDiagnosticPatient(idOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'orders', 'receive'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'orders', 'review'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}

export function useReceiveDiagnosticPatientsBatch() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: ReceiveDiagnosticPatientsBatchRequest) =>
      receiveDiagnosticPatientsBatch(payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'orders', 'receive'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'orders', 'review'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'worklist'] });
      void queryClient.invalidateQueries({ queryKey: ['diagnostic', 'dashboard'] });
    },
  });
}
