import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { CANCELLED_STATUSES } from '../data/appointment-list-mock';
import { buildMockQueueStatus } from '../data/appointment-queue-mock';
import type { AppointmentQueueStatus } from '../types/appointment-queue.types';
import { fetchAppointmentDetailByIdApi } from './appointment-detail.api';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

export async function fetchAppointmentQueueStatusApi(
  appointmentId: string,
): Promise<AppointmentQueueStatus> {
  const cleanId = appointmentId.trim();
  if (!cleanId) {
    throw new Error('Mã lịch hẹn không hợp lệ.');
  }

  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const response = await apiClient.get<AppointmentQueueStatus>(
        `/appointments/${encodeURIComponent(cleanId)}/queue-status`,
      );
      return response.data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  const appointment = await fetchAppointmentDetailByIdApi(cleanId);
  if (CANCELLED_STATUSES.includes(appointment.status)) {
    throw new Error('Lịch hẹn đã hủy nên không còn số thứ tự trong hàng đợi.');
  }

  return buildMockQueueStatus(appointment);
}
