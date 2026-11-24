import { AxiosError, type AxiosResponse } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import {
  getExaminationServiceDetailApi,
  searchExaminationServicesApi,
} from '../api/service-search.api';
import { MOCK_EXAMINATION_SERVICES } from '../data/service-search-mock';
import type { ServiceSearchResponse } from '../types/service-search.types';

describe('service-search.api (M1-42 pure HTTP)', () => {
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

  it('propagates network, 400 and 500 errors in searchExaminationServicesApi without mock fallback', async () => {
    const networkError = new AxiosError('Network Error', 'ERR_NETWORK');
    const getSpy = vi.spyOn(apiClient, 'get').mockRejectedValueOnce(networkError);
    await expect(
      searchExaminationServicesApi({ department: 'dept-th', page: 1, pageSize: 6 }),
    ).rejects.toBe(networkError);

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

    getSpy.mockRejectedValueOnce(badRequestError);

    await expect(searchExaminationServicesApi({ pageSize: 100 })).rejects.toThrow(
      'Request failed with status code 400',
    );
  });

  it('calls GET /examination-services/{id}, returns null on blank id, and propagates network/400 errors', async () => {
    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValueOnce({ data: MOCK_EXAMINATION_SERVICES[0] } as AxiosResponse);

    expect(await getExaminationServiceDetailApi('   ')).toBeNull();
    expect(getSpy).not.toHaveBeenCalled();

    const detail = await getExaminationServiceDetailApi('EX-GI-01');
    expect(getSpy).toHaveBeenCalledWith('/examination-services/EX-GI-01');
    expect(detail?.id).toBe(MOCK_EXAMINATION_SERVICES[0].id);

    const networkError = new AxiosError('Network Error', 'ERR_NETWORK');
    getSpy.mockRejectedValueOnce(networkError);
    await expect(getExaminationServiceDetailApi('DV-TH-01')).rejects.toBe(networkError);

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
    getSpy.mockRejectedValueOnce(badRequestError);

    await expect(getExaminationServiceDetailApi('INVALID@CODE')).rejects.toThrow(
      'Request failed with status code 400',
    );
  });
});
