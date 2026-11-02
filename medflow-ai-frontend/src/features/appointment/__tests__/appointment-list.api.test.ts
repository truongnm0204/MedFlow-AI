import { AxiosError } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { fetchPatientAppointmentsApi } from '../api/appointment-list.api';
import { MOCK_APPOINTMENTS } from '../data/appointment-list-mock';
import type { AppointmentListResponse } from '../types/appointment-list.types';

describe('appointment-list.api (HTTP + mock fallback)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('returns mock response without calling apiClient.get when access token is absent', async () => {
    const getSpy = vi.spyOn(apiClient, 'get');

    const result = await fetchPatientAppointmentsApi();

    expect(getSpy).not.toHaveBeenCalled();
    expect(result.items.length).toBeGreaterThan(0);
    expect(result.totalAllMembersCount).toBe(MOCK_APPOINTMENTS.length);
  });

  it('calls GET /appointments/me with default filter params when access token is present', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');

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

  it('falls back to mock data when backend returns 404 AxiosError', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');

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

    const result = await fetchPatientAppointmentsApi({ tab: 'UPCOMING' });

    expect(getSpy).toHaveBeenCalledTimes(1);
    expect(result.items.length).toBeGreaterThan(0);
    expect(result.page).toBe(1);
  });

  it('falls back to mock data when apiClient.get rejects with a network AxiosError without response', async () => {
    localStorage.setItem('medflow.access_token', 'test-jwt-token');

    const networkError = new AxiosError('Network Error', 'ERR_NETWORK');
    const getSpy = vi.spyOn(apiClient, 'get').mockRejectedValueOnce(networkError);

    const result = await fetchPatientAppointmentsApi();

    expect(getSpy).toHaveBeenCalledTimes(1);
    expect(result.totalAllMembersCount).toBe(MOCK_APPOINTMENTS.length);
  });
});
