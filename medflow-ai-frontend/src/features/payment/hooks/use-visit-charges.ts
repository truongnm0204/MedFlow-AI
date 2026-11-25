import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';

import { useRealtimeSubscription } from '@/shared/hooks/use-realtime-subscription';
import { scheduleCoalescedInvalidation, type RealtimeChannel } from '@/shared/lib/realtime-client';

import { fetchVisitChargesApi } from '../api/payment.api';
import type { VisitChargesResponseDto } from '../types/payment.types';
import { PAYMENT_POLL_INTERVAL_MS } from '../utils/payment-format';

const PAYMENT_CONNECTED_POLL_INTERVAL_MS = 15_000;

function isTerminalPaymentState(data: VisitChargesResponseDto | undefined): boolean {
  if (!data) return false;
  if (data.settlementStatus === 'PAID') return true;
  if (data.charges.length === 0) {
    const status = data.paymentStatus?.toUpperCase() ?? '';
    return status === 'CANCELLED' || status === 'FAILED';
  }
  return false;
}

function hasAnyPendingPayment(data: VisitChargesResponseDto | undefined): boolean {
  if (!data) return false;
  if (data.paymentStatus?.toUpperCase() === 'PENDING') return true;
  return data.charges.some((line) => line.paymentStatus?.toUpperCase() === 'PENDING');
}

export function useVisitChargesQuery(visitIdOrCode: string) {
  const queryClient = useQueryClient();
  const trimmed = visitIdOrCode.trim();
  const queryKey = useMemo(() => ['visit-charges', visitIdOrCode] as const, [visitIdOrCode]);

  const cachedData = queryClient.getQueryData<VisitChargesResponseDto>(queryKey);
  const shouldSubscribe = Boolean(trimmed) && !isTerminalPaymentState(cachedData);

  const channel = useMemo<RealtimeChannel | null>(
    () => (shouldSubscribe ? { type: 'visit-payment', visitIdOrCode: trimmed } : null),
    [shouldSubscribe, trimmed],
  );

  const invalidatePaymentQueries = useCallback(() => {
    if (!trimmed) return;
    scheduleCoalescedInvalidation(queryClient, ['visit-charges', visitIdOrCode]);
    scheduleCoalescedInvalidation(queryClient, ['payments', 'charges', visitIdOrCode]);
    scheduleCoalescedInvalidation(queryClient, ['my-visits']);
  }, [queryClient, trimmed, visitIdOrCode]);

  const { isLive } = useRealtimeSubscription({
    channel,
    event: 'VisitPaymentChanged',
    onEvent: invalidatePaymentQueries,
    onReconnected: invalidatePaymentQueries,
    enabled: shouldSubscribe,
  });

  return useQuery({
    queryKey,
    queryFn: () => fetchVisitChargesApi(visitIdOrCode),
    enabled: Boolean(trimmed),
    retry: false,
    refetchInterval: (query) => {
      if (!hasAnyPendingPayment(query.state.data)) {
        return false;
      }
      return isLive ? PAYMENT_CONNECTED_POLL_INTERVAL_MS : PAYMENT_POLL_INTERVAL_MS;
    },
  });
}
