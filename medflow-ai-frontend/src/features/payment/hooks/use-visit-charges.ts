import { useQuery } from '@tanstack/react-query';

import { fetchVisitChargesApi } from '../api/payment.api';
import { PAYMENT_POLL_INTERVAL_MS } from '../utils/payment-format';

export function useVisitChargesQuery(visitIdOrCode: string) {
  return useQuery({
    queryKey: ['visit-charges', visitIdOrCode],
    queryFn: () => fetchVisitChargesApi(visitIdOrCode),
    enabled: Boolean(visitIdOrCode.trim()),
    retry: false,
    refetchInterval: (query) =>
      query.state.data?.paymentStatus?.toUpperCase() === 'PENDING'
        ? PAYMENT_POLL_INTERVAL_MS
        : false,
  });
}
