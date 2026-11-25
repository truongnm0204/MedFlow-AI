import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';

import { useRealtimeSubscription } from '@/shared/hooks/use-realtime-subscription';
import { scheduleCoalescedInvalidation, type RealtimeChannel } from '@/shared/lib/realtime-client';

import { fetchVisitChargesApi, generateQrPaymentApi } from '../api/payment.api';
import type { VisitChargesResponse } from '../types/payment.types';

const PAYMENT_DISCONNECTED_POLL_MS = 5_000;
const PAYMENT_CONNECTED_POLL_MS = 15_000;

const chargesKey = (visitCode: string) => ['payments', 'charges', visitCode] as const;

/** Poll 15 giây khi đang kết nối SignalR, 5 giây dự phòng khi mất kết nối cho mã PENDING. */
export function useVisitChargesQuery(visitCode: string) {
  const queryClient = useQueryClient();
  const trimmed = visitCode.trim();
  const key = useMemo(() => chargesKey(visitCode), [visitCode]);

  const cached = queryClient.getQueryData<VisitChargesResponse>(key);
  const isTerminal =
    cached?.settlementStatus === 'PAID' ||
    cached?.paymentStatus === 'PAID' ||
    cached?.paymentStatus === 'CANCELLED' ||
    cached?.paymentStatus === 'FAILED';

  const shouldSubscribe = Boolean(trimmed) && !isTerminal;
  const channel = useMemo<RealtimeChannel | null>(
    () => (shouldSubscribe ? { type: 'visit-payment', visitIdOrCode: trimmed } : null),
    [shouldSubscribe, trimmed],
  );

  const invalidatePaymentQueries = useCallback(() => {
    if (!trimmed) return;
    scheduleCoalescedInvalidation(queryClient, chargesKey(visitCode));
    scheduleCoalescedInvalidation(queryClient, ['visit-charges', visitCode]);
    scheduleCoalescedInvalidation(queryClient, ['my-visits']);
  }, [queryClient, trimmed, visitCode]);

  const { isLive } = useRealtimeSubscription({
    channel,
    event: 'VisitPaymentChanged',
    onEvent: invalidatePaymentQueries,
    onReconnected: invalidatePaymentQueries,
    enabled: shouldSubscribe,
  });

  return useQuery({
    queryKey: key,
    queryFn: () => fetchVisitChargesApi(visitCode),
    enabled: Boolean(visitCode),
    refetchInterval: (query) => {
      if (query.state.data?.paymentStatus !== 'PENDING') {
        return false;
      }
      return isLive ? PAYMENT_CONNECTED_POLL_MS : PAYMENT_DISCONNECTED_POLL_MS;
    },
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
