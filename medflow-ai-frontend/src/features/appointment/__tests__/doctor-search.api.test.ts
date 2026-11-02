import { AxiosError } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { fetchDoctorsApi, searchMockDoctors } from '../api/doctor-search.api';
import type { DoctorSearchResponse } from '../types/doctor-search.types';

describe('doctor-search.api (M1-38 HTTP + fallback)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('calls GET /doctors via apiClient with normalized query params and returns backend payload', async () => {
    const payload: DoctorSearchResponse = {
      items: [
        {
          id: '2',
          name: 'ThS.BS Trần Thu Hà',
          title: 'ThS',
          roleTag: 'Thạc sĩ y khoa',
          specialty: 'Khoa Tim mạch',
          specialtyId: 'tim-mach',
          yearsOfExperience: 11,
          room: 'Phòng 201 - Tầng 1, Khu Demo',
          nearestSchedule: 'Ngày mai: 09:00 - 09:30',
          scheduleStatus: 'FEW_SLOTS',
          scheduleStatusText: 'Còn 3 lượt',
          price: 220000,
          avatarUrl: '/assets/admin-doctors/list-02.png',
          hasScheduleToday: false,
          hasScheduleThisWeek: true,
        },
      ],
      total: 1,
      page: 1,
      pageSize: 6,
      totalPages: 1,
    };

    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: payload });

    const result = await fetchDoctorsApi({ keyword: '  Thu Hà ', specialty: 'tim-mach' });

    expect(getSpy).toHaveBeenCalledWith('/doctors', {
      params: {
        keyword: 'Thu Hà',
        specialty: 'tim-mach',
        degree: 'ALL',
        hasScheduleToday: false,
        hasScheduleThisWeek: false,
        page: 1,
        pageSize: 6,
      },
    });
    expect(result).toEqual(payload);
  });

  it('falls back to searchMockDoctors when backend returns 404', async () => {
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

    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(notFoundError);

    const result = await fetchDoctorsApi({});

    expect(result).toEqual(searchMockDoctors({}));
    expect(result.items[0].id).toBe('doc-01');
  });

  it('rethrows error when backend returns 400 validation error', async () => {
    const badRequestError = new AxiosError(
      'Request failed with status code 400',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      {
        status: 400,
        statusText: 'Bad Request',
        headers: {},
        config: {} as never,
        data: {
          status: 400,
          title: 'Dữ liệu không hợp lệ',
          detail: 'Học hàm/học vị không hợp lệ.',
        },
      },
    );

    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(badRequestError);

    await expect(fetchDoctorsApi({ degree: 'XYZ' as never })).rejects.toBe(badRequestError);
  });
});
