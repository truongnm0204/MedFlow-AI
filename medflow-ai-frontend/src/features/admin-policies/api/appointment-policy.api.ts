import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import {
  getMockAppointmentPolicy,
  updateMockAppointmentPolicy,
} from '../data/appointment-policy-mock';
import type {
  AppointmentPolicyDto,
  UpdateAppointmentPolicyRequest,
} from '../types/appointment-policy.types';

function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  return !error.response || error.response.status === 404;
}

function shouldUseHttpApi(): boolean {
  if ('mock' in apiClient.get || 'mock' in apiClient.put) return true;
  if (import.meta.env.MODE === 'test') return false;
  return (
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'))
  );
}

export async function fetchAppointmentPolicyApi(): Promise<AppointmentPolicyDto> {
  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.get<AppointmentPolicyDto>('/admin/appointment-policy');
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return getMockAppointmentPolicy();
}

export async function updateAppointmentPolicyApi(
  payload: UpdateAppointmentPolicyRequest,
): Promise<AppointmentPolicyDto> {
  if (shouldUseHttpApi()) {
    try {
      const { data } = await apiClient.put<AppointmentPolicyDto>(
        '/admin/appointment-policy',
        payload,
      );
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  return updateMockAppointmentPolicy(payload);
}
