import { apiClient } from '@/shared/lib/axios';

import type {
  GetNotificationsParams,
  MarkAllNotificationsReadResponse,
  MarkNotificationReadResponse,
  NotificationFeedResponse,
} from '../types/appointment-reminder.types';

export async function fetchNotificationsApi(
  params: GetNotificationsParams = {},
): Promise<NotificationFeedResponse> {
  const { limit = 30, unreadOnly = false } = params;
  const { data } = await apiClient.get<NotificationFeedResponse>('/notifications', {
    params: { limit, unreadOnly },
  });
  return data;
}

export async function markNotificationReadApi(
  notificationId: string,
): Promise<MarkNotificationReadResponse> {
  const { data } = await apiClient.patch<MarkNotificationReadResponse>(
    `/notifications/${encodeURIComponent(notificationId)}/read`,
  );
  return data;
}

export async function markAllNotificationsReadApi(): Promise<MarkAllNotificationsReadResponse> {
  const { data } =
    await apiClient.post<MarkAllNotificationsReadResponse>('/notifications/read-all');
  return data;
}
