import { AxiosError, type AxiosResponse } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import {
  getExaminationServiceDetailApi,
  searchExaminationServicesApi,
} from '../api/service-search.api';
import { MOCK_EXAMINATION_SERVICES } from '../data/service-search-mock';
import type { ServiceSearchResponse } from '../types/service-search.types';

describe('service-search.api (M1-42 HTTP + fallback)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('calls GET /examination-services and returns backend response when API succeeds', async () => {
    const fakeResponse: ServiceSearchResponse = {
      data: [MOCK_EXAMINATION_SERVICES[0]],
      total: 1,
      page: 1,
      pageSize: 6,
      totalPages: 1,
    };

    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValueOnce({ data: fakeResponse } as AxiosResponse<ServiceSearchResponse>);

    const result = await searchExaminationServicesApi({
      query: 'Tiêu hóa',
      category: 'specialty',
      department: 'dept-th',
      priceDurationRange: 'under-200k',
      page: 1,
      pageSize: 6,
    });

    expect(getSpy).toHaveBeenCalledWith('/examination-services', {
      params: {
        query: 'Tiêu hóa',
        category: 'specialty',
        department: 'dept-th',
        priceDurationRange: 'under-200k',
        page: 1,
        pageSize: 6,
      },
    });
    expect(result).toEqual(fakeResponse);
  });

  it('falls back to in-memory mock when backend is offline (network error)', async () => {
    const networkError = new AxiosError('Network Error', 'ERR_NETWORK');
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(networkError);

    const result = await searchExaminationServicesApi({
      department: 'dept-th',
      page: 1,
      pageSize: 6,
    });

    expect(result.data.length).toBeGreaterThan(0);
    expect(result.data.every((s) => s.departmentId === 'dept-th')).toBe(true);
  });

  it('rethrows validation/server errors (400/500) instead of falling back to mock', async () => {
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
        data: { status: 400, title: 'Lỗi xác thực', detail: 'Kích thước trang phải từ 1 đến 50.' },
      },
    );

    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(badRequestError);

    await expect(searchExaminationServicesApi({ pageSize: 100 })).rejects.toThrow(
      'Request failed with status code 400',
    );
  });

  it('calls GET /examination-services/{id} for service detail and falls back on network error', async () => {
    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValueOnce({ data: MOCK_EXAMINATION_SERVICES[0] } as AxiosResponse);

    const detail = await getExaminationServiceDetailApi('EX-GI-01');
    expect(getSpy).toHaveBeenCalledWith('/examination-services/EX-GI-01');
    expect(detail?.id).toBe(MOCK_EXAMINATION_SERVICES[0].id);

    getSpy.mockRejectedValueOnce(new AxiosError('Network Error', 'ERR_NETWORK'));
    const fallbackDetail = await getExaminationServiceDetailApi('DV-TH-01');
    expect(fallbackDetail?.serviceCode).toBe('DV-TH-01');
  });

  it('rethrows 400 BadRequest in getExaminationServiceDetailApi instead of falling back to mock', async () => {
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
        data: { status: 400, title: 'Lỗi xác thực', detail: 'Mã dịch vụ khám không hợp lệ.' },
      },
    );

    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(badRequestError);

    await expect(getExaminationServiceDetailApi('INVALID@CODE')).rejects.toThrow(
      'Request failed with status code 400',
    );
  });
});
