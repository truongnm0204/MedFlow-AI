import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { fetchQueuePolicyApi, updateQueuePolicyApi } from '../api/queue-policy.api';
import { DEFAULT_QUEUE_POLICY_VALUES } from '../data/queue-policy-defaults';
import type { QueuePolicyDto, UpdateQueuePolicyRequest } from '../types/queue-policy.types';

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

describe('queue-policy.api (UC55)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetchQueuePolicyApi calls /admin/queue-policy and returns the server DTO', async () => {
    const serverDto: QueuePolicyDto = {
      ...DEFAULT_QUEUE_POLICY_VALUES,
      graceMinutes: 10,
      lastUpdatedAt: '2026-11-15T08:30:00Z',
    };
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: serverDto });

    const result = await fetchQueuePolicyApi();

    expect(getSpy).toHaveBeenCalledWith('/admin/queue-policy');
    expect(result).toEqual(serverDto);
  });

  it('fetchQueuePolicyApi rejects on HTTP 404 and on network error without fallback value', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(makeAxiosError(404, 'Not found'));
    await expect(fetchQueuePolicyApi()).rejects.toThrow(AxiosError);

    const networkErr = new AxiosError('Network Error', 'ERR_NETWORK');
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(networkErr);
    await expect(fetchQueuePolicyApi()).rejects.toThrow(AxiosError);
  });

  it('updateQueuePolicyApi PUTs the exact payload to /admin/queue-policy and returns the response DTO', async () => {
    const payload: UpdateQueuePolicyRequest = {
      graceMinutes: 20,
      expectedDurationMinutes: 30,
      safetyBufferMinutes: 10,
      walkinPriorityRatio: { priority: 4, normal: 2 },
      publicNumberImmutable: false,
      manualPriorityRequiresVerification: true,
    };
    const updatedDto: QueuePolicyDto = {
      ...payload,
      lastUpdatedAt: '2026-11-16T09:00:00Z',
    };
    const putSpy = vi.spyOn(apiClient, 'put').mockResolvedValueOnce({ data: updatedDto });

    const result = await updateQueuePolicyApi(payload);

    expect(putSpy).toHaveBeenCalledWith('/admin/queue-policy', payload);
    expect(result).toEqual(updatedDto);
  });

  it('updateQueuePolicyApi rejects on HTTP 404, network error, and HTTP 400 without resolving with a mock', async () => {
    vi.spyOn(apiClient, 'put').mockRejectedValueOnce(makeAxiosError(404, 'Not found'));
    await expect(updateQueuePolicyApi(DEFAULT_QUEUE_POLICY_VALUES)).rejects.toThrow(AxiosError);

    const networkErr = new AxiosError('Network Error', 'ERR_NETWORK');
    vi.spyOn(apiClient, 'put').mockRejectedValueOnce(networkErr);
    await expect(updateQueuePolicyApi(DEFAULT_QUEUE_POLICY_VALUES)).rejects.toThrow(AxiosError);

    vi.spyOn(apiClient, 'put').mockRejectedValueOnce(
      makeAxiosError(400, 'Thời gian ân hạn đúng giờ không hợp lệ.'),
    );
    await expect(updateQueuePolicyApi(DEFAULT_QUEUE_POLICY_VALUES)).rejects.toThrow(AxiosError);
  });
});
