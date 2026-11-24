import { AxiosError } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { ACCESS_TOKEN_KEY } from '@/shared/lib/auth-storage';
import { apiClient } from '@/shared/lib/axios';

import { fetchPatientAppointmentsApi } from '../api/appointment-list.api';
import { MOCK_APPOINTMENTS } from '../data/appointment-list-mock';
import type { AppointmentListResponse } from '../types/appointment-list.types';

describe('appointment-list.api (pure HTTP)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('calls GET /appointments/me with default filter params and returns backend payload', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'test-jwt-token');

    const backendPayload: AppointmentListResponse = {
      items: [MOCK_APPOINTMENTS[0]],
      total: 1,
      page: 1,
      pageSize: 10,
      totalPages: 1,
      tabCounts: { all: 1, upcoming: 1, completed: 0, cancelled: 0 },
      memberCounts: [
        {
          id: '1',
          patientCode: 'PAT-0001',
          fullName: 'Nguyễn Văn An',
          relationshipLabel: 'Bản thân',
          ageLabel: '46 tuổi',
          appointmentCount: 1,
        },
      ],
      totalAllMembersCount: 1,
    };

    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: backendPayload });

    const result = await fetchPatientAppointmentsApi();

    expect(getSpy).toHaveBeenCalledWith('/appointments/me', {
      params: {
        memberId: 'ALL',
        tab: 'ALL',
        keyword: '',
        dateFrom: '',
        dateTo: '',
        page: 1,
        pageSize: 10,
      },
    });
    expect(result).toEqual(backendPayload);
  });

  it('preserves empty backend response without injecting mock appointments', async () => {
    const emptyPayload: AppointmentListResponse = {
      items: [],
      total: 0,
      page: 1,
      pageSize: 10,
      totalPages: 0,
      tabCounts: { all: 0, upcoming: 0, completed: 0, cancelled: 0 },
      memberCounts: [],
      totalAllMembersCount: 0,
    };

    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: emptyPayload });

    const result = await fetchPatientAppointmentsApi({ tab: 'UPCOMING' });
    expect(result.items).toEqual([]);
    expect(result.totalAllMembersCount).toBe(0);
  });

  it('propagates HTTP 401, 404 and network errors without falling back to mock data', async () => {
    const notFoundError = new AxiosError(
      'Request failed with status code 404',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      {
        status: 404,
        statusText: 'Not Found',
        headers: {},
        config: {} as never,
        data: { status: 404, title: 'Không tìm thấy' },
      },
    );

    const getSpy = vi.spyOn(apiClient, 'get').mockRejectedValueOnce(notFoundError);
    await expect(fetchPatientAppointmentsApi({ tab: 'UPCOMING' })).rejects.toBe(notFoundError);

    const networkError = new AxiosError('Network Error', 'ERR_NETWORK');
    getSpy.mockRejectedValueOnce(networkError);
    await expect(fetchPatientAppointmentsApi()).rejects.toBe(networkError);
  });
});
