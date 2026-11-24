import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY } from '@/shared/lib/auth-storage';

import type { UserDto } from '../types/auth.types';

interface MockStoreState {
  user: UserDto | null;
  isAuthenticated: boolean;
  sessionEpoch: number;
  refreshToken: string | null;
  setTokens: ReturnType<typeof vi.fn>;
  tryApplyRefreshedTokens?: (payload: {
    accessToken: string;
    refreshToken: string;
    expectedRefreshToken?: string;
    expectedUserId?: number | null;
    expectedEpoch?: number;
  }) => boolean;
  clearAuth: ReturnType<typeof vi.fn>;
}

const mockSetTokens = vi.fn(
  ({ accessToken, refreshToken }: { accessToken: string; refreshToken: string }) => {
    localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
    mockStoreState.refreshToken = refreshToken;
  },
);

const mockClearAuth = vi.fn(() => {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
  mockStoreState.isAuthenticated = false;
  mockStoreState.user = null;
  mockStoreState.refreshToken = null;
  mockStoreState.sessionEpoch += 1;
});

let mockStoreState: MockStoreState = {
  user: {
    id: 1,
    email: 'patient01@medflow.test',
    fullName: 'BN 1',
    roles: ['PATIENT'],
    patientCode: 'PAT-0001',
  },
  isAuthenticated: true,
  sessionEpoch: 1,
  refreshToken: null,
  setTokens: mockSetTokens,
  clearAuth: mockClearAuth,
};

// Mock axios module trước khi import apiClient.
vi.mock('axios', async () => {
  const actual = await vi.importActual<typeof import('axios')>('axios');
  return {
    ...actual,
    default: {
      ...actual.default,
      post: vi.fn(),
      create: vi.fn((config) => actual.default.create(config)),
    },
  };
});

// Mock dynamic import auth.store — tránh circular dep trong test.
vi.mock('@/features/auth/store/auth.store', () => ({
  useAuthStore: {
    getState: () => mockStoreState,
  },
}));

const {
  API_BASE_URL,
  apiClient,
  parseRetryAfterMs,
  RefreshCooldownError,
  resetRefreshCoordinatorForTests,
  resolveApiBaseUrl,
} = await import('@/shared/lib/axios');

const cfg = (url = '/test', bearerToken?: string) =>
  ({
    url,
    headers: bearerToken ? { Authorization: `Bearer ${bearerToken}` } : {},
  }) as InternalAxiosRequestConfig & { _retried?: boolean; _retryCount?: number };

/** Trích xuất response interceptor handler từ axios internals. */
function getResponseInterceptor() {
  const interceptors = (
    apiClient.interceptors.response as unknown as {
      handlers: Array<{ fulfilled?: unknown; rejected?: (e: AxiosError) => Promise<unknown> }>;
    }
  ).handlers;
  return interceptors.find((h) => h.rejected)?.rejected;
}

describe('axios interceptor — 401 refresh, error classification & multi-tab locks', () => {
  const originalLocation = window.location;
  const mockLocationAssign = vi.fn();

  beforeEach(() => {
    localStorage.clear();
    resetRefreshCoordinatorForTests();
    vi.clearAllMocks();

    mockStoreState = {
      user: {
        id: 1,
        email: 'patient01@medflow.test',
        fullName: 'BN 1',
        roles: ['PATIENT'],
        patientCode: 'PAT-0001',
      },
      isAuthenticated: true,
      sessionEpoch: 1,
      refreshToken: null,
      setTokens: mockSetTokens,
      clearAuth: mockClearAuth,
    };

    Object.defineProperty(window, 'location', {
      configurable: true,
      value: {
        ...originalLocation,
        pathname: '/reception/routing',
        search: '?visitId=VIS-01',
        assign: mockLocationAssign,
      },
    });

    // Stub adapter dispatch để retry không gọi network thật trong jsdom.
    apiClient.defaults.adapter = vi.fn((_config: unknown) =>
      Promise.resolve({ data: {}, status: 200, statusText: 'OK', headers: {}, config: _config }),
    ) as never;
  });

  afterEach(() => {
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: originalLocation,
    });
  });

  it('resolveApiBaseUrl chuẩn hóa VITE_API_BASE_URL và dùng chung API_BASE_URL cho apiClient', () => {
    expect(resolveApiBaseUrl(undefined)).toBe('/api/v1');
    expect(resolveApiBaseUrl('')).toBe('/api/v1');
    expect(resolveApiBaseUrl('   ')).toBe('/api/v1');
    expect(resolveApiBaseUrl('/api/v1/')).toBe('/api/v1');
    expect(resolveApiBaseUrl('https://api.medflow.vn/api/v1///')).toBe(
      'https://api.medflow.vn/api/v1',
    );
    expect(apiClient.defaults.baseURL).toBe(API_BASE_URL);
  });

  it('bỏ qua refresh cho auth endpoints (kể cả /auth/logout)', async () => {
    const axiosModule = await import('axios');
    const handler = getResponseInterceptor();
    expect(handler).toBeDefined();

    const loginErr = new AxiosError('401', undefined, cfg('/auth/login'), {}, {
      status: 401,
    } as never);
    await expect(handler!(loginErr)).rejects.toThrow();

    const logoutErr = new AxiosError('401', undefined, cfg('/auth/logout'), {}, {
      status: 401,
    } as never);
    await expect(handler!(logoutErr)).rejects.toThrow();
    expect(axiosModule.default.post).not.toHaveBeenCalled();
  });

  it('không refresh khi không có refresh token', async () => {
    mockStoreState.isAuthenticated = false;
    const axiosModule = await import('axios');
    const handler = getResponseInterceptor();
    expect(handler).toBeDefined();

    const err = new AxiosError('401', undefined, cfg('/me'), {}, { status: 401 } as never);
    await expect(handler!(err)).rejects.toThrow();
    expect(axiosModule.default.post).not.toHaveBeenCalled();
    expect(mockLocationAssign).not.toHaveBeenCalled();
  });

  it('bỏ qua request đã retry (_retried)', async () => {
    const handler = getResponseInterceptor();
    expect(handler).toBeDefined();

    const config = cfg('/me');
    config._retried = true;
    const err = new AxiosError('401', undefined, config, {}, { status: 401 } as never);
    await expect(handler!(err)).rejects.toThrow();
  });

  it('bỏ qua non-401 errors', async () => {
    const handler = getResponseInterceptor();
    expect(handler).toBeDefined();

    const err = new AxiosError('500', undefined, cfg('/me'), {}, { status: 500 } as never);
    await expect(handler!(err)).rejects.toThrow();
  });

  it('happy path: 401 → refresh qua API_BASE_URL → retry request gốc với token mới', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'at-old');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'rt-old');
    mockStoreState.refreshToken = 'rt-old';

    const axiosModule = await import('axios');
    vi.mocked(axiosModule.default.post).mockResolvedValueOnce({
      data: {
        accessToken: 'at-new',
        accessTokenExpiresIn: 3600,
        refreshToken: 'rt-new',
        refreshTokenExpiresIn: 604800,
      },
    });

    const handler = getResponseInterceptor();
    const config = cfg('/me', 'at-old');
    const err = new AxiosError('401', undefined, config, {}, { status: 401 } as never);
    await handler!(err);

    expect(axiosModule.default.post).toHaveBeenCalledWith(
      `${API_BASE_URL}/auth/refresh`,
      { refreshToken: 'rt-old' },
      expect.objectContaining({ timeout: 15_000 }),
    );
    expect(mockSetTokens).toHaveBeenCalledWith({ accessToken: 'at-new', refreshToken: 'rt-new' });
    expect(config.headers.Authorization).toBe('Bearer at-new');
    expect(config._retried).toBe(true);
    expect(config._retryCount).toBe(1);
  });

  it('single-flight: nhiều 401 đồng thời trong cùng tab chỉ gọi refresh 1 lần', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'at-old');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'rt-old');
    mockStoreState.refreshToken = 'rt-old';

    const axiosModule = await import('axios');
    vi.mocked(axiosModule.default.post).mockResolvedValueOnce({
      data: {
        accessToken: 'at-new',
        accessTokenExpiresIn: 3600,
        refreshToken: 'rt-new',
        refreshTokenExpiresIn: 604800,
      },
    });

    const handler = getResponseInterceptor();
    const err1 = new AxiosError('401', undefined, cfg('/a', 'at-old'), {}, {
      status: 401,
    } as never);
    const err2 = new AxiosError('401', undefined, cfg('/b', 'at-old'), {}, {
      status: 401,
    } as never);
    const err3 = new AxiosError('401', undefined, cfg('/c', 'at-old'), {}, {
      status: 401,
    } as never);

    await Promise.all([handler!(err1), handler!(err2), handler!(err3)]);

    expect(axiosModule.default.post).toHaveBeenCalledTimes(1);
  });

  it('clearAuth + redirect bảo toàn query string khi /auth/refresh trả 401 thật sự', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'at-old');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'rt-expired');
    mockStoreState.refreshToken = 'rt-expired';

    const axiosModule = await import('axios');
    vi.mocked(axiosModule.default.post).mockRejectedValueOnce(
      new AxiosError('Unauthorized', undefined, cfg(`${API_BASE_URL}/auth/refresh`), {}, {
        status: 401,
      } as never),
    );

    const handler = getResponseInterceptor();
    const err = new AxiosError('401', undefined, cfg('/me', 'at-old'), {}, {
      status: 401,
    } as never);

    await expect(handler!(err)).rejects.toThrow();
    expect(mockClearAuth).toHaveBeenCalledTimes(1);
    expect(mockLocationAssign).toHaveBeenCalledWith(
      '/login?returnUrl=%2Freception%2Frouting%3FvisitId%3DVIS-01',
    );
  });

  it('không logout khi /auth/refresh gặp 429 và tôn trọng Retry-After cooldown', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'at-old');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'rt-valid');
    mockStoreState.refreshToken = 'rt-valid';

    const axiosModule = await import('axios');
    vi.mocked(axiosModule.default.post).mockRejectedValueOnce(
      new AxiosError('Too Many Requests', undefined, cfg(`${API_BASE_URL}/auth/refresh`), {}, {
        status: 429,
        headers: { 'retry-after': '45' },
      } as never),
    );

    const handler = getResponseInterceptor();
    const err1 = new AxiosError('401', undefined, cfg('/me', 'at-old'), {}, {
      status: 401,
    } as never);
    await expect(handler!(err1)).rejects.toThrow();

    expect(mockClearAuth).not.toHaveBeenCalled();
    expect(mockLocationAssign).not.toHaveBeenCalled();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('rt-valid');

    // Trong thời gian Retry-After cooldown, request 401 kế tiếp bị chặn sớm (không gọi thêm /auth/refresh).
    const err2 = new AxiosError('401', undefined, cfg('/appointments', 'at-old'), {}, {
      status: 401,
    } as never);
    await expect(handler!(err2)).rejects.toBeInstanceOf(RefreshCooldownError);
    expect(axiosModule.default.post).toHaveBeenCalledTimes(1);
    expect(mockClearAuth).not.toHaveBeenCalled();
  });

  it.each([
    { label: '500 Internal Server Error', code: undefined, status: 500 },
    { label: '503 Service Unavailable', code: undefined, status: 503 },
    { label: 'Network Error', code: 'ERR_NETWORK', status: undefined },
    { label: 'Timeout ECONNABORTED', code: 'ECONNABORTED', status: undefined },
  ])('không logout khi /auth/refresh gặp lỗi tạm thời: $label', async ({ label, code, status }) => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'at-old');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'rt-valid');
    mockStoreState.refreshToken = 'rt-valid';

    const axiosModule = await import('axios');
    const refreshErr = new AxiosError(
      label,
      code,
      cfg(`${API_BASE_URL}/auth/refresh`),
      {},
      status ? ({ status } as never) : undefined,
    );
    vi.mocked(axiosModule.default.post).mockRejectedValueOnce(refreshErr);

    const handler = getResponseInterceptor();
    const err = new AxiosError('401', undefined, cfg('/me', 'at-old'), {}, {
      status: 401,
    } as never);
    await expect(handler!(err)).rejects.toThrow();

    expect(mockClearAuth).not.toHaveBeenCalled();
    expect(mockLocationAssign).not.toHaveBeenCalled();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('rt-valid');
  });

  it('multi-tab Web Locks: sau khi lấy lock, nếu tab khác đã rotate token thì dùng luôn token mới mà không gọi /auth/refresh', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'at-old');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'rt-old');
    mockStoreState.refreshToken = 'rt-old';

    const originalLocks = navigator.locks;
    const mockRequest = vi.fn(
      async (_name: string, _opts: unknown, callback: () => Promise<unknown>) => {
        localStorage.setItem(ACCESS_TOKEN_KEY, 'at-rotated-by-tab-a');
        localStorage.setItem(REFRESH_TOKEN_KEY, 'rt-rotated-by-tab-a');
        return callback();
      },
    );
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: { request: mockRequest },
    });

    try {
      const axiosModule = await import('axios');
      const handler = getResponseInterceptor();
      const config = cfg('/patients/me/visits', 'at-old');
      const err = new AxiosError('401', undefined, config, {}, { status: 401 } as never);

      await handler!(err);

      expect(mockRequest).toHaveBeenCalledTimes(1);
      expect(axiosModule.default.post).not.toHaveBeenCalled();
      expect(config.headers.Authorization).toBe('Bearer at-rotated-by-tab-a');
    } finally {
      Object.defineProperty(navigator, 'locks', {
        configurable: true,
        value: originalLocks,
      });
    }
  });

  it('chống khôi phục phiên đã logout khi response refresh đến muộn', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'at-old');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'rt-old');
    mockStoreState.refreshToken = 'rt-old';

    let resolveRefresh!: (val: unknown) => void;
    const axiosModule = await import('axios');
    vi.mocked(axiosModule.default.post).mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveRefresh = resolve;
        }),
    );

    const handler = getResponseInterceptor();
    const config = cfg('/patients/me/visits', 'at-old');
    const err = new AxiosError('401', undefined, config, {}, { status: 401 } as never);

    const pending = handler!(err);

    await vi.waitFor(() => expect(axiosModule.default.post).toHaveBeenCalledTimes(1));
    mockClearAuth();

    resolveRefresh({
      data: {
        accessToken: 'at-late-must-discard',
        accessTokenExpiresIn: 3600,
        refreshToken: 'rt-late-must-discard',
        refreshTokenExpiresIn: 604800,
      },
    });

    await expect(pending).rejects.toThrow(/logged_out/);
    expect(mockSetTokens).not.toHaveBeenCalled();
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
    expect(apiClient.defaults.adapter).not.toHaveBeenCalled();
  });

  it('chống ghi đè hoặc xoá nhầm phiên khi tài khoản khác vừa đăng nhập trong lúc refresh đang bay', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'at-u1');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'rt-u1');
    mockStoreState.refreshToken = 'rt-u1';

    let rejectRefresh!: (reason: unknown) => void;
    const axiosModule = await import('axios');
    vi.mocked(axiosModule.default.post).mockImplementationOnce(
      () =>
        new Promise((_resolve, reject) => {
          rejectRefresh = reject;
        }),
    );

    const handler = getResponseInterceptor();
    const err = new AxiosError('401', undefined, cfg('/me', 'at-u1'), {}, { status: 401 } as never);
    const pending = handler!(err);

    await vi.waitFor(() => expect(axiosModule.default.post).toHaveBeenCalledTimes(1));

    mockStoreState.user = {
      id: 2,
      email: 'doctor01@medflow.test',
      fullName: 'BS 2',
      roles: ['DOCTOR'],
      patientCode: null,
      staffCode: 'STF-001',
    };
    mockStoreState.isAuthenticated = true;
    mockStoreState.sessionEpoch = 3;
    mockStoreState.refreshToken = 'rt-u2';
    localStorage.setItem(ACCESS_TOKEN_KEY, 'at-u2');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'rt-u2');

    rejectRefresh(
      new AxiosError('Unauthorized', undefined, cfg(`${API_BASE_URL}/auth/refresh`), {}, {
        status: 401,
      } as never),
    );

    await expect(pending).rejects.toThrow();
    expect(mockClearAuth).not.toHaveBeenCalled();
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe('at-u2');
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('rt-u2');
  });

  it('parseRetryAfterMs xử lý đúng delta-seconds, HTTP-date và giới hạn biên', () => {
    const fixedNow = Date.UTC(2026, 10, 24, 10, 0, 0);
    expect(parseRetryAfterMs('30', fixedNow)).toBe(30_000);
    expect(parseRetryAfterMs('999', fixedNow)).toBe(120_000);
    expect(parseRetryAfterMs('Tue, 24 Nov 2026 10:00:15 GMT', fixedNow)).toBe(15_000);
    expect(parseRetryAfterMs('invalid-header', fixedNow)).toBeNull();
  });
});
