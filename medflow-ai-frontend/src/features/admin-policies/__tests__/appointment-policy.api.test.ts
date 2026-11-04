import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import {
  fetchAppointmentPolicyApi,
  updateAppointmentPolicyApi,
} from '../api/appointment-policy.api';
import {
  DEFAULT_APPOINTMENT_POLICY_VALUES,
  INITIAL_MOCK_APPOINTMENT_POLICY,
  resetMockAppointmentPolicy,
} from '../data/appointment-policy-mock';
import type { AppointmentPolicyDto } from '../types/appointment-policy.types';

function makeAxiosError(status: number, detail: string): AxiosError {
  const config = { headers: {} } as InternalAxiosRequestConfig;
  const response: AxiosResponse = {
    data: { detail },
    status,
    statusText: String(status),
    headers: {},
    config,
  };
  return new AxiosError(detail, `ERR_BAD_REQUEST_${status}`, config, {}, response);
}

describe('appointment-policy.api (UC54)', () => {
  beforeEach(() => {
    resetMockAppointmentPolicy();
    window.localStorage.setItem('medflow.access_token', 'test-jwt-token');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    window.localStorage.clear();
  });

  it('fetchAppointmentPolicyApi returns server AppointmentPolicyDto on HTTP 200', async () => {
    const serverDto: AppointmentPolicyDto = {
      ...DEFAULT_APPOINTMENT_POLICY_VALUES,
      bookingWindowDays: 60,
      lastUpdatedAt: '2026-11-15T08:30:00Z',
    };
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: serverDto });

    const result = await fetchAppointmentPolicyApi();

    expect(getSpy).toHaveBeenCalledWith('/admin/appointment-policy');
    expect(result).toEqual(serverDto);
  });

  it('fetchAppointmentPolicyApi falls back to mock on HTTP 404 or network error', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(makeAxiosError(404, 'Not found'));

    const result404 = await fetchAppointmentPolicyApi();
    expect(result404).toEqual(INITIAL_MOCK_APPOINTMENT_POLICY);

    const networkErr = new AxiosError('Network Error', 'ERR_NETWORK');
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(networkErr);

    const resultNetwork = await fetchAppointmentPolicyApi();
    expect(resultNetwork).toEqual(INITIAL_MOCK_APPOINTMENT_POLICY);
  });

  it('fetchAppointmentPolicyApi rethrows AxiosError on HTTP 403 without falling back to mock', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(
      makeAxiosError(403, 'Tài khoản quản trị chưa được gán bệnh viện.'),
    );

    await expect(fetchAppointmentPolicyApi()).rejects.toThrow(AxiosError);
  });

  it('updateAppointmentPolicyApi sends all 10 integer fields to PUT /admin/appointment-policy and returns updated DTO', async () => {
    const payload = {
      ...DEFAULT_APPOINTMENT_POLICY_VALUES,
      bookingWindowDays: 45,
      noShowBlockDays: 0,
    };
    const updatedDto: AppointmentPolicyDto = {
      ...payload,
      lastUpdatedAt: '2026-11-16T09:00:00Z',
    };
    const putSpy = vi.spyOn(apiClient, 'put').mockResolvedValueOnce({ data: updatedDto });

    const result = await updateAppointmentPolicyApi(payload);

    expect(putSpy).toHaveBeenCalledWith('/admin/appointment-policy', payload);
    expect(result).toEqual(updatedDto);
  });

  it('updateAppointmentPolicyApi rethrows HTTP 400 and HTTP 403 errors without falling back to mock', async () => {
    vi.spyOn(apiClient, 'put').mockRejectedValueOnce(
      makeAxiosError(400, 'Thời gian đặt trước tối thiểu phải nhỏ hơn cửa sổ đặt lịch.'),
    );

    await expect(updateAppointmentPolicyApi(DEFAULT_APPOINTMENT_POLICY_VALUES)).rejects.toThrow(
      AxiosError,
    );

    vi.spyOn(apiClient, 'put').mockRejectedValueOnce(
      makeAxiosError(403, 'Tài khoản quản trị chưa được gán bệnh viện.'),
    );

    await expect(updateAppointmentPolicyApi(DEFAULT_APPOINTMENT_POLICY_VALUES)).rejects.toThrow(
      AxiosError,
    );
  });
});
