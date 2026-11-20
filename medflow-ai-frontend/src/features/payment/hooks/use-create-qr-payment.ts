import { useMutation, useQueryClient } from '@tanstack/react-query';

import { createQrPaymentApi } from '../api/payment.api';

export function useCreateQrPaymentMutation(visitIdOrCode: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => createQrPaymentApi(visitIdOrCode),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['visit-charges', visitIdOrCode] });
    },
  });
}
