import { QueryClientContext, useMutation, useQuery } from '@tanstack/react-query';
import { useContext, useEffect, useRef } from 'react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { queryClient as globalQueryClient } from '@/shared/lib/query-client';

import {
  fetchNotificationsApi,
  markAllNotificationsReadApi,
  markNotificationReadApi,
} from '../api/notifications.api';
import type {
  GetNotificationsParams,
  NotificationFeedResponse,
} from '../types/appointment-reminder.types';

export const NOTIFICATIONS_ROOT_KEY = 'notifications' as const;

export function buildNotificationsQueryKey(
  userId: number | null,
  params: GetNotificationsParams = {},
) {
  const { limit = 30, unreadOnly = false } = params;
  return [NOTIFICATIONS_ROOT_KEY, userId, { limit, unreadOnly }] as const;
}

// Purge global notifications cache immediately on logout or account switch.
useAuthStore.subscribe((state, prevState) => {
  const loggedOut = prevState.isAuthenticated && !state.isAuthenticated;
  const switchedAccount = prevState.user?.id !== undefined && state.user?.id !== prevState.user?.id;

  if (loggedOut || switchedAccount) {
    globalQueryClient.removeQueries({ queryKey: [NOTIFICATIONS_ROOT_KEY] });
  }
});

function useResolvedQueryClient() {
  const contextClient = useContext(QueryClientContext);
  return contextClient ?? globalQueryClient;
}

interface UseNotificationsOptions extends GetNotificationsParams {
  enabled?: boolean;
  refetchIntervalMs?: number | false;
}

export function useNotificationsQuery(options: UseNotificationsOptions = {}) {
  const { limit = 30, unreadOnly = false, enabled = true, refetchIntervalMs = 30_000 } = options;
  const qc = useResolvedQueryClient();
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const userId = useAuthStore((s) => s.user?.id ?? null);
  const roles = useAuthStore((s) => s.user?.roles);
  const previousUserIdRef = useRef<number | null>(userId);

  useEffect(() => {
    const prevUserId = previousUserIdRef.current;
    if (!isAuthenticated) {
      qc.removeQueries({ queryKey: [NOTIFICATIONS_ROOT_KEY] });
    } else if (prevUserId !== null && prevUserId !== userId) {
      qc.removeQueries({ queryKey: [NOTIFICATIONS_ROOT_KEY, prevUserId] });
    }
    previousUserIdRef.current = userId;
  }, [isAuthenticated, qc, userId]);

  const isPatientPortalEligible =
    isAuthenticated &&
    userId !== null &&
    (roles === undefined || roles.length === 0 || roles.includes('PATIENT'));

  return useQuery(
    {
      queryKey: buildNotificationsQueryKey(userId, { limit, unreadOnly }),
      queryFn: () => fetchNotificationsApi({ limit, unreadOnly }),
      enabled: enabled && isPatientPortalEligible,
      refetchInterval: enabled && isPatientPortalEligible ? refetchIntervalMs : false,
      staleTime: 10_000,
      retry: false,
    },
    qc,
  );
}

interface OptimisticNotificationSnapshot {
  entries: Array<[readonly unknown[], NotificationFeedResponse | undefined]>;
}

export function useMarkNotificationReadMutation() {
  const qc = useResolvedQueryClient();
  const userId = useAuthStore((s) => s.user?.id ?? null);

  return useMutation(
    {
      mutationFn: (notificationId: string) => markNotificationReadApi(notificationId),
      onMutate: async (notificationId): Promise<OptimisticNotificationSnapshot> => {
        if (userId === null) {
          return { entries: [] };
        }

        const prefix = [NOTIFICATIONS_ROOT_KEY, userId] as const;
        await qc.cancelQueries({ queryKey: prefix });

        const entries = qc.getQueriesData<NotificationFeedResponse>({ queryKey: prefix });

        qc.setQueriesData<NotificationFeedResponse>({ queryKey: prefix }, (current) => {
          if (!current) return current;
          const target = current.items.find((i) => i.id === notificationId);
          if (!target || target.isRead) return current;

          return {
            ...current,
            unreadCount: Math.max(0, current.unreadCount - 1),
            // activeReminder stays untouched — banner visibility depends on Appointment status, not ReadAt.
            items: current.items.map((item) =>
              item.id === notificationId ? { ...item, isRead: true } : item,
            ),
          };
        });

        return { entries };
      },
      onError: (_err, _notificationId, context) => {
        if (!context) return;
        for (const [key, snapshot] of context.entries) {
          qc.setQueryData(key, snapshot);
        }
      },
      onSuccess: (response) => {
        if (userId === null) return;
        qc.setQueriesData<NotificationFeedResponse>(
          { queryKey: [NOTIFICATIONS_ROOT_KEY, userId] },
          (current) =>
            current
              ? {
                  ...current,
                  unreadCount: response.unreadCount,
                  items: current.items.map((item) =>
                    item.id === response.id ? { ...item, isRead: true } : item,
                  ),
                }
              : current,
        );
      },
    },
    qc,
  );
}

export function useMarkAllNotificationsReadMutation() {
  const qc = useResolvedQueryClient();
  const userId = useAuthStore((s) => s.user?.id ?? null);

  return useMutation(
    {
      mutationFn: () => markAllNotificationsReadApi(),
      onMutate: async (): Promise<OptimisticNotificationSnapshot> => {
        if (userId === null) {
          return { entries: [] };
        }

        const prefix = [NOTIFICATIONS_ROOT_KEY, userId] as const;
        await qc.cancelQueries({ queryKey: prefix });

        const entries = qc.getQueriesData<NotificationFeedResponse>({ queryKey: prefix });

        qc.setQueriesData<NotificationFeedResponse>({ queryKey: prefix }, (current) => {
          if (!current) return current;
          return {
            ...current,
            unreadCount: 0,
            // Keep activeReminder intact when marking notifications as read.
            items: current.items.map((item) => ({ ...item, isRead: true })),
          };
        });

        return { entries };
      },
      onError: (_err, _vars, context) => {
        if (!context) return;
        for (const [key, snapshot] of context.entries) {
          qc.setQueryData(key, snapshot);
        }
      },
      onSuccess: (response) => {
        if (userId === null) return;
        qc.setQueriesData<NotificationFeedResponse>(
          { queryKey: [NOTIFICATIONS_ROOT_KEY, userId] },
          (current) =>
            current
              ? {
                  ...current,
                  unreadCount: response.unreadCount,
                  items: current.items.map((item) => ({ ...item, isRead: true })),
                }
              : current,
        );
      },
    },
    qc,
  );
}
