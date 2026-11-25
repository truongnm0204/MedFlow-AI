import { useQuery, useQueryClient } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { useCallback, useEffect, useMemo, useRef } from 'react';

import { useRealtimeSubscription } from '@/shared/hooks/use-realtime-subscription';
import {
  refreshRealtimeChannelScope,
  scheduleCoalescedInvalidation,
  type RealtimeChannel,
} from '@/shared/lib/realtime-client';

import { fetchAppointmentQueueStatusApi } from '../api/appointment-queue.api';
import type { AppointmentQueueStatus } from '../types/appointment-queue.types';

const QUEUE_CONNECTED_POLL_MS = 30_000;
const QUEUE_FALLBACK_POLL_MS = 5_000;

function isNonRetryableQueueHttpError(error: unknown): boolean {
  if (!isAxiosError(error)) return false;
  const status = error.response?.status;
  return status === 401 || status === 403 || status === 404 || status === 409;
}

export function useAppointmentQueueStatusQuery(appointmentId: string) {
  const queryClient = useQueryClient();
  const trimmed = appointmentId.trim();
  const queryKey = useMemo(
    () => ['appointments', 'queue', appointmentId] as const,
    [appointmentId],
  );

  const cached = queryClient.getQueryData<AppointmentQueueStatus>(queryKey);
  const shouldSubscribe = Boolean(trimmed) && cached?.status !== 'COMPLETED';

  const channel = useMemo<RealtimeChannel | null>(
    () => (shouldSubscribe ? { type: 'appointment-queue', appointmentIdOrCode: trimmed } : null),
    [shouldSubscribe, trimmed],
  );

  const invalidateQueueQuery = useCallback(() => {
    if (!trimmed) return;
    scheduleCoalescedInvalidation(queryClient, ['appointments', 'queue', appointmentId], {
      coalesceMs: 250,
      maxJitterMs: 120,
    });
  }, [appointmentId, queryClient, trimmed]);

  const { connectionState, isLive } = useRealtimeSubscription({
    channel,
    event: 'QueueChanged',
    onEvent: invalidateQueueQuery,
    onReconnected: invalidateQueueQuery,
    enabled: shouldSubscribe,
  });

  const query = useQuery({
    queryKey,
    queryFn: () => fetchAppointmentQueueStatusApi(appointmentId),
    enabled: Boolean(trimmed),
    retry: false,
    refetchInterval: (q) => {
      if (q.state.data?.status === 'COMPLETED') return false;
      if (q.state.status === 'error' && isNonRetryableQueueHttpError(q.state.error)) return false;
      return isLive ? QUEUE_CONNECTED_POLL_MS : QUEUE_FALLBACK_POLL_MS;
    },
  });

  // Khi bệnh nhân được chuyển sang buồng khám mới, đồng bộ lại patient-bucket tương ứng trên Hub.
  const previousRoomRef = useRef<string | null>(null);
  const currentRoom = query.data?.roomName ?? null;
  useEffect(() => {
    if (!trimmed || !currentRoom) return;
    if (previousRoomRef.current !== null && previousRoomRef.current !== currentRoom && channel) {
      refreshRealtimeChannelScope(channel);
    }
    previousRoomRef.current = currentRoom;
  }, [channel, currentRoom, trimmed]);

  return {
    ...query,
    isRealtimeLive: isLive,
    realtimeStatus: connectionState,
  };
}
