import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { useRealtimeSubscription } from '@/shared/hooks/use-realtime-subscription';
import { scheduleCoalescedInvalidation, type RealtimeChannel } from '@/shared/lib/realtime-client';

import { fetchDiagnosticDashboardSummary } from '../api/diagnostic.api';

const HOSPITAL_QUEUE_CHANNEL: RealtimeChannel = { type: 'hospital-queue' };

export function useDiagnosticDashboard(enabled = true) {
  const queryClient = useQueryClient();
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);

  const handleRealtimeQueueChanged = useCallback(() => {
    scheduleCoalescedInvalidation(queryClient, ['diagnostic', 'dashboard']);
  }, [queryClient]);

  const { isLive } = useRealtimeSubscription({
    channel: HOSPITAL_QUEUE_CHANNEL,
    event: 'QueueChanged',
    onEvent: handleRealtimeQueueChanged,
    onReconnected: handleRealtimeQueueChanged,
    enabled: enabled && isAuthenticated,
  });

  return useQuery({
    queryKey: ['diagnostic', 'dashboard'],
    queryFn: () => fetchDiagnosticDashboardSummary(),
    enabled,
    staleTime: 60_000,
    refetchInterval: enabled && isAuthenticated ? (isLive ? 30_000 : 15_000) : false,
  });
}
