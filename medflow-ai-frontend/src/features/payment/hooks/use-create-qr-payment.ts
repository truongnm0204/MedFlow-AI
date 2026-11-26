import { useMutation, useQueryClient } from '@tanstack/react-query';

import {
  declineDiagnosticOrderApi,
  generateQrPaymentApi,
  type QrPaymentCreateInput,
  updateDiagnosticOrderSelectionApi,
} from '../api/payment.api';
import type {
  DeclineDiagnosticOrderRequest,
  UpdateDiagnosticOrderSelectionRequest,
} from '../types/payment.types';

export function useCreateQrPaymentMutation(visitIdOrCode: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input?: QrPaymentCreateInput) => generateQrPaymentApi(visitIdOrCode, input),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['visit-charges', visitIdOrCode] });
    },
  });
}

export function useUpdateDiagnosticSelectionMutation(visitIdOrCode: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: UpdateDiagnosticOrderSelectionRequest) =>
      updateDiagnosticOrderSelectionApi(visitIdOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['visit-charges', visitIdOrCode] });
    },
  });
}

export function useDeclineDiagnosticOrderMutation(visitIdOrCode: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      orderIdOrCode,
      payload,
    }: {
      orderIdOrCode: number | string;
      payload?: DeclineDiagnosticOrderRequest;
    }) => declineDiagnosticOrderApi(visitIdOrCode, orderIdOrCode, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['visit-charges', visitIdOrCode] });
    },
  });
}
