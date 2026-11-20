import { useMutation, useQueryClient } from '@tanstack/react-query';

import { generateQrPaymentApi } from '../api/payment.api';

export function useCreateQrPaymentMutation(visitIdOrCode: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => generateQrPaymentApi(visitIdOrCode),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['visit-charges', visitIdOrCode] });
    },
  });
}
