import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

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
    localStorage.setItem('medflow.access_token', accessToken);
    localStorage.setItem('medflow.refresh_token', refreshToken);
    mockStoreState.refreshToken = refreshToken;
  },
);

const mockClearAuth = vi.fn(() => {
  localStorage.removeItem('medflow.access_token');
  localStorage.removeItem('medflow.refresh_token');
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
      create: vi.fn(() => actual.default.create()),
    },
  };
});

// Mock dynamic import auth.store — tránh circular dep trong test.
vi.mock('@/features/auth/store/auth.store', () => ({
  useAuthStore: {
    getState: () => mockStoreState,
  },
}));

const { apiClient, parseRetryAfterMs, RefreshCooldownError, resetRefreshCoordinatorForTests } =
  await import('@/shared/lib/axios');

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

  it('happy path: 401 → refresh → retry request gốc với token mới', async () => {
    localStorage.setItem('medflow.access_token', 'at-old');
    localStorage.setItem('medflow.refresh_token', 'rt-old');
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
      '/api/v1/auth/refresh',
      { refreshToken: 'rt-old' },
      expect.objectContaining({ timeout: 15_000 }),
    );
    expect(mockSetTokens).toHaveBeenCalledWith({ accessToken: 'at-new', refreshToken: 'rt-new' });
    expect(config.headers.Authorization).toBe('Bearer at-new');
    expect(config._retried).toBe(true);
    expect(config._retryCount).toBe(1);
  });

  it('single-flight: nhiều 401 đồng thời trong cùng tab chỉ gọi refresh 1 lần', async () => {
    localStorage.setItem('medflow.access_token', 'at-old');
    localStorage.setItem('medflow.refresh_token', 'rt-old');
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
    localStorage.setItem('medflow.access_token', 'at-old');
    localStorage.setItem('medflow.refresh_token', 'rt-expired');
    mockStoreState.refreshToken = 'rt-expired';

    const axiosModule = await import('axios');
    vi.mocked(axiosModule.default.post).mockRejectedValueOnce(
      new AxiosError('Unauthorized', undefined, cfg('/api/v1/auth/refresh'), {}, {
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
    localStorage.setItem('medflow.access_token', 'at-old');
    localStorage.setItem('medflow.refresh_token', 'rt-valid');
    mockStoreState.refreshToken = 'rt-valid';

    const axiosModule = await import('axios');
    vi.mocked(axiosModule.default.post).mockRejectedValueOnce(
      new AxiosError('Too Many Requests', undefined, cfg('/api/v1/auth/refresh'), {}, {
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
    expect(localStorage.getItem('medflow.refresh_token')).toBe('rt-valid');

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
    localStorage.setItem('medflow.access_token', 'at-old');
    localStorage.setItem('medflow.refresh_token', 'rt-valid');
    mockStoreState.refreshToken = 'rt-valid';

    const axiosModule = await import('axios');
    const refreshErr = new AxiosError(
      label,
      code,
      cfg('/api/v1/auth/refresh'),
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
    expect(localStorage.getItem('medflow.refresh_token')).toBe('rt-valid');
  });

  it('multi-tab Web Locks: sau khi lấy lock, nếu tab khác đã rotate token thì dùng luôn token mới mà không gọi /auth/refresh', async () => {
    localStorage.setItem('medflow.access_token', 'at-old');
    localStorage.setItem('medflow.refresh_token', 'rt-old');
    mockStoreState.refreshToken = 'rt-old';

    // Giả lập Web Locks API: trong lúc Tab B đang chờ lock, Tab A hoàn tất rotate trong localStorage.
    const originalLocks = navigator.locks;
    const mockRequest = vi.fn(
      async (_name: string, _opts: unknown, callback: () => Promise<unknown>) => {
        localStorage.setItem('medflow.access_token', 'at-rotated-by-tab-a');
        localStorage.setItem('medflow.refresh_token', 'rt-rotated-by-tab-a');
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
    localStorage.setItem('medflow.access_token', 'at-old');
    localStorage.setItem('medflow.refresh_token', 'rt-old');
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

    // Cho phép doRefresh tiến vào lời gọi axios.post, sau đó user bấm Logout trước khi mạng trả về.
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
    expect(localStorage.getItem('medflow.access_token')).toBeNull();
    expect(localStorage.getItem('medflow.refresh_token')).toBeNull();
    expect(apiClient.defaults.adapter).not.toHaveBeenCalled();
  });

  it('chống ghi đè hoặc xoá nhầm phiên khi tài khoản khác vừa đăng nhập trong lúc refresh đang bay', async () => {
    localStorage.setItem('medflow.access_token', 'at-u1');
    localStorage.setItem('medflow.refresh_token', 'rt-u1');
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

    // Tab khác đăng xuất User 1 và đăng nhập User 2 trong lúc request refresh của User 1 đang bay.
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
    localStorage.setItem('medflow.access_token', 'at-u2');
    localStorage.setItem('medflow.refresh_token', 'rt-u2');

    // Request refresh cũ của User 1 trả 401 muộn → tuyệt đối KHÔNG được clearAuth của User 2!
    rejectRefresh(
      new AxiosError('Unauthorized', undefined, cfg('/api/v1/auth/refresh'), {}, {
        status: 401,
      } as never),
    );

    await expect(pending).rejects.toThrow();
    expect(mockClearAuth).not.toHaveBeenCalled();
    expect(localStorage.getItem('medflow.access_token')).toBe('at-u2');
    expect(localStorage.getItem('medflow.refresh_token')).toBe('rt-u2');
  });

  it('parseRetryAfterMs xử lý đúng delta-seconds, HTTP-date và giới hạn biên', () => {
    const fixedNow = Date.UTC(2026, 10, 24, 10, 0, 0);
    expect(parseRetryAfterMs('30', fixedNow)).toBe(30_000);
    expect(parseRetryAfterMs('999', fixedNow)).toBe(120_000);
    expect(parseRetryAfterMs('Tue, 24 Nov 2026 10:00:15 GMT', fixedNow)).toBe(15_000);
    expect(parseRetryAfterMs('invalid-header', fixedNow)).toBeNull();
  });
});
