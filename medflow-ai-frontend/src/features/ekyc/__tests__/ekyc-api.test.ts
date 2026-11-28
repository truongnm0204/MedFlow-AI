import type { AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi, type Mock } from 'vitest';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

import { apiClient } from '@/shared/lib/axios';

import { createEkycSession, finalizeEkycSession, getEkycSession } from '../api/ekyc.api';
import type {
  CreateEkycSessionRequest,
  CreateEkycSessionResponse,
  EkycSessionStatusResponse,
  FinalizeEkycSessionResponse,
} from '../types/ekyc.types';

const client = apiClient as unknown as { get: Mock; post: Mock };

function axiosResponse<T>(data: T): AxiosResponse<T> {
  return {
    data,
    status: 200,
    statusText: 'OK',
    headers: {},
    config: {},
  } as unknown as AxiosResponse<T>;
}

describe('ekyc.api', () => {
  beforeEach(() => {
    client.get.mockReset();
    client.post.mockReset();
  });

  describe('createEkycSession', () => {
    const payload: CreateEkycSessionRequest = {
      consentVersion: 'v2025.1',
      consentAccepted: true,
    };
    const response: CreateEkycSessionResponse = {
      sessionId: 'sess-123',
      proxyBasePath: '/api/v1/ekyc/proxy/sess-123',
      status: 'INITIATED',
      expiresAt: '2026-11-28T10:00:00Z',
    };

    it('POST đúng /ekyc/sessions với payload đồng ý và trả về data', async () => {
      client.post.mockResolvedValueOnce(axiosResponse(response));

      const result = await createEkycSession(payload);

      expect(client.post).toHaveBeenCalledTimes(1);
      expect(client.post).toHaveBeenCalledWith('/ekyc/sessions', payload);
      expect(client.post.mock.calls[0]).toHaveLength(2);
      expect(result).toBe(response);
    });

    it('không gọi GET và không gọi endpoint nào khác khi tạo phiên', async () => {
      client.post.mockResolvedValueOnce(axiosResponse(response));

      await createEkycSession(payload);

      expect(client.get).not.toHaveBeenCalled();
      expect(client.post.mock.calls[0][0]).toBe('/ekyc/sessions');
    });

    it('lan truyền lỗi từ apiClient cho caller', async () => {
      const error = new Error('network down');
      client.post.mockRejectedValueOnce(error);

      await expect(createEkycSession(payload)).rejects.toBe(error);
    });
  });

  describe('getEkycSession', () => {
    const response: EkycSessionStatusResponse = {
      sessionId: 'sess-123',
      status: 'DOCUMENT_BACK_DONE',
      verificationMode: 'SELF_SERVICE',
      purpose: 'ACCOUNT_ACTIVATION',
      bindingState: 'NOT_APPLICABLE',
      attemptCount: 1,
      expiresAt: '2026-11-28T10:00:00Z',
      expired: false,
      failureReason: null,
      createdAt: '2026-11-28T09:50:00Z',
    };

    it('GET đúng /ekyc/sessions/<id> và trả về data', async () => {
      client.get.mockResolvedValueOnce(axiosResponse(response));

      const result = await getEkycSession('sess-123');

      expect(client.get).toHaveBeenCalledTimes(1);
      expect(client.get).toHaveBeenCalledWith('/ekyc/sessions/sess-123');
      expect(client.get.mock.calls[0]).toHaveLength(1);
      expect(result).toBe(response);
    });

    it('mã hoá phiên trong URL, không gửi body', async () => {
      client.get.mockResolvedValueOnce(axiosResponse(response));

      await getEkycSession('abc-def');

      expect(client.get.mock.calls[0]).toEqual(['/ekyc/sessions/abc-def']);
      expect(client.post).not.toHaveBeenCalled();
    });

    it('lan truyền lỗi từ apiClient cho caller', async () => {
      const error = new Error('forbidden');
      client.get.mockRejectedValueOnce(error);

      await expect(getEkycSession('sess-123')).rejects.toBe(error);
    });
  });

  describe('finalizeEkycSession', () => {
    const response: FinalizeEkycSessionResponse = {
      sessionId: 'sess-123',
      status: 'VERIFIED',
      failureReason: null,
      failureCode: null,
      consumedAt: '2026-11-28T10:00:00Z',
      accountActivated: true,
      activationOutcome: 'ACTIVATED',
      activationReviewReason: null,
    };

    it('POST đúng /ekyc/sessions/<id>/finalize KHÔNG kèm body và trả về data', async () => {
      client.post.mockResolvedValueOnce(axiosResponse(response));

      const result = await finalizeEkycSession('sess-123');

      expect(client.post).toHaveBeenCalledTimes(1);
      expect(client.post).toHaveBeenCalledWith('/ekyc/sessions/sess-123/finalize');
      // Chỉ một đối số: body không được gửi (verdict do backend quyết định).
      expect(client.post.mock.calls[0]).toHaveLength(1);
      expect(result).toBe(response);
    });

    it('không gọi GET và không gửi verdict/evidence lên server', async () => {
      client.post.mockResolvedValueOnce(axiosResponse(response));

      await finalizeEkycSession('sess-123');

      expect(client.get).not.toHaveBeenCalled();
      expect(client.post.mock.calls[0]).toEqual(['/ekyc/sessions/sess-123/finalize']);
    });

    it('lan truyền lỗi từ apiClient cho caller', async () => {
      const error = new Error('finalize failed');
      client.post.mockRejectedValueOnce(error);

      await expect(finalizeEkycSession('sess-123')).rejects.toBe(error);
    });
  });
});
