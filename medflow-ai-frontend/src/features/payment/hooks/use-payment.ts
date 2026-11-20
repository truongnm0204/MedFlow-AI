import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { fetchVisitChargesApi, generateQrPaymentApi } from '../api/payment.api';

const chargesKey = (visitCode: string) => ['payments', 'charges', visitCode] as const;

/** Poll 5 giây chỉ khi còn mã đang chờ; giữ nguyên dữ liệu cũ trong lúc chờ. */
export function useVisitChargesQuery(visitCode: string) {
  return useQuery({
    queryKey: chargesKey(visitCode),
    queryFn: () => fetchVisitChargesApi(visitCode),
    enabled: Boolean(visitCode),
    refetchInterval: (query) => (query.state.data?.paymentStatus === 'PENDING' ? 5000 : false),
    refetchIntervalInBackground: false,
  });
}

export function useGenerateQrPaymentMutation(visitCode: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => generateQrPaymentApi(visitCode),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: chargesKey(visitCode) }),
  });
}
