import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import { createMockAppointment } from '../data/appointment-booking-mock';
import type { AppointmentListItem } from '../types/appointment-list.types';
import type { CreateAppointmentRequest } from '../types/booking-review.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

export async function createAppointmentApi(
  request: CreateAppointmentRequest,
): Promise<AppointmentListItem> {
  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const response = await apiClient.post<AppointmentListItem>('/appointments', request);
      return response.data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return createMockAppointment(request);
}
