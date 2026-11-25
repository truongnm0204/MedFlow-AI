import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { useRealtimeSubscription } from '@/shared/hooks/use-realtime-subscription';
import { scheduleCoalescedInvalidation, type RealtimeChannel } from '@/shared/lib/realtime-client';

import { fetchDoctorDashboardSummary } from '../api/doctor.api';
import type { DoctorDashboardQueryParams } from '../types/doctor.types';

const HOSPITAL_QUEUE_CHANNEL: RealtimeChannel = { type: 'hospital-queue' };

export function useDoctorDashboard(params?: DoctorDashboardQueryParams, enabled = true) {
  const queryClient = useQueryClient();
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);

  const handleRealtimeQueueChanged = useCallback(() => {
    scheduleCoalescedInvalidation(queryClient, ['doctor', 'dashboard']);
  }, [queryClient]);

  const { isLive } = useRealtimeSubscription({
    channel: HOSPITAL_QUEUE_CHANNEL,
    event: 'QueueChanged',
    onEvent: handleRealtimeQueueChanged,
    onReconnected: handleRealtimeQueueChanged,
    enabled: enabled && isAuthenticated,
  });

  return useQuery({
    queryKey: ['doctor', 'dashboard', params ?? null],
    queryFn: () => fetchDoctorDashboardSummary(params),
    enabled,
    staleTime: 60_000,
    refetchInterval: enabled && isAuthenticated ? (isLive ? 30_000 : 15_000) : false,
  });
}
