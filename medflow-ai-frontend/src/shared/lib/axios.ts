import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';

import { ACCESS_TOKEN_KEY, REFRESH_COOLDOWN_KEY, REFRESH_TOKEN_KEY } from './auth-storage';
import { withCrossTabLock } from './cross-tab-lock';

const REFRESH_LOCK_NAME = 'medflow:auth:refresh';

const MAX_REQUEST_AUTH_RETRIES = 1;
const MAX_CONSECUTIVE_TRANSIENT_FAILURES = 3;
const MIN_RETRY_AFTER_MS = 1_000;
const MAX_RETRY_AFTER_MS = 120_000;
const DEFAULT_TRANSIENT_COOLDOWN_MS = 10_000;

/** Chuẩn hóa base URL từ biến môi trường (fallback `/api/v1`) và cắt bỏ dấu `/` thừa ở cuối. */
export function resolveApiBaseUrl(rawBaseUrl?: unknown): string {
  if (typeof rawBaseUrl !== 'string') return '/api/v1';
  const trimmed = rawBaseUrl.trim();
  if (!trimmed) return '/api/v1';
  return trimmed.replace(/\/+$/, '') || '/api/v1';
}

export const API_BASE_URL = resolveApiBaseUrl(import.meta.env.VITE_API_BASE_URL);

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { 'Content-Type': 'application/json' },
  timeout: 15_000,
});

// Attach auth token when present.
apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem(ACCESS_TOKEN_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// --- 401 → silent refresh (intra-tab single-flight + cross-tab Web Locks) ---

interface RefreshResponse {
  accessToken: string;
  accessTokenExpiresIn: number;
  refreshToken: string;
  refreshTokenExpiresIn: number;
}

type RetriableConfig = InternalAxiosRequestConfig & {
  _retried?: boolean;
  _retryCount?: number;
};

type StaleDiscardReason = 'logged_out' | 'session_replaced';

class NoRefreshTokenError extends Error {
  constructor() {
    super('No refresh token');
    this.name = 'NoRefreshTokenError';
  }
}

export class RefreshCooldownError extends Error {
  readonly retryAfterMs: number;

  constructor(retryAfterMs: number) {
    super(`Refresh temporarily throttled (${retryAfterMs}ms remaining)`);
    this.name = 'RefreshCooldownError';
    this.retryAfterMs = retryAfterMs;
  }
}

export class StaleRefreshDiscardedError extends Error {
  readonly reason: StaleDiscardReason;

  constructor(reason: StaleDiscardReason) {
    super(`Discarded stale refresh response (${reason})`);
    this.name = 'StaleRefreshDiscardedError';
    this.reason = reason;
  }
}

let refreshPromise: Promise<string> | null = null;
let authStoreModulePromise: Promise<typeof import('@/features/auth/store/auth.store')> | null =
  null;
let consecutiveTransientFailures = 0;
let trackedFailureRefreshToken: string | null = null;

/** Tải lười (lazy single-flight) `useAuthStore` để tránh circular dependency với store. */
async function loadAuthStore() {
  authStoreModulePromise ??= import('@/features/auth/store/auth.store');
  const mod = await authStoreModulePromise;
  return mod.useAuthStore;
}

/** Phân tích header `Retry-After` (delta-seconds hoặc HTTP-date) ra mili-giây hợp lệ. */
export function parseRetryAfterMs(headerValue: unknown, nowMs = Date.now()): number | null {
  if (headerValue === undefined || headerValue === null) return null;

  const raw = Array.isArray(headerValue) ? String(headerValue[0] ?? '') : String(headerValue);
  const trimmed = raw.trim();
  if (!trimmed) return null;

  if (/^\d+(\.\d+)?$/.test(trimmed)) {
    const seconds = Number(trimmed);
    if (!Number.isFinite(seconds) || seconds <= 0) return MIN_RETRY_AFTER_MS;
    return Math.min(MAX_RETRY_AFTER_MS, Math.max(MIN_RETRY_AFTER_MS, Math.ceil(seconds * 1_000)));
  }

  const targetMs = Date.parse(trimmed);
  if (!Number.isNaN(targetMs)) {
    const deltaMs = targetMs - nowMs;
    if (deltaMs <= 0) return 0;
    return Math.min(MAX_RETRY_AFTER_MS, Math.max(MIN_RETRY_AFTER_MS, deltaMs));
  }

  return null;
}

/** Chỉ huỷ phiên khi chắc chắn refresh token không còn hợp lệ hoặc bị thu hồi (401/403). */
export function shouldInvalidateSessionOnRefreshError(error: unknown): boolean {
  if (error instanceof NoRefreshTokenError) return true;
  if (error instanceof StaleRefreshDiscardedError || error instanceof RefreshCooldownError) {
    return false;
  }

  if (axios.isAxiosError(error)) {
    const status = error.response?.status;
    return status === 401 || status === 403;
  }

  return false;
}

/** Reset bộ đếm cooldown/retry phục vụ kiểm thử đơn vị độc lập. */
export function resetRefreshCoordinatorForTests(): void {
  refreshPromise = null;
  authStoreModulePromise = null;
  consecutiveTransientFailures = 0;
  trackedFailureRefreshToken = null;
  if (typeof localStorage !== 'undefined') {
    localStorage.removeItem(REFRESH_COOLDOWN_KEY);
  }
}

function extractBearerToken(authHeader: unknown): string | null {
  if (typeof authHeader !== 'string') return null;
  const match = /^Bearer\s+(.+)$/i.exec(authHeader.trim());
  return match?.[1]?.trim() || null;
}

function readCooldownUntilMs(): number {
  const raw = localStorage.getItem(REFRESH_COOLDOWN_KEY);
  if (!raw) return 0;
  const parsed = Number(raw);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
}

function writeCooldownUntilMs(untilMs: number): void {
  if (untilMs > Date.now()) {
    localStorage.setItem(REFRESH_COOLDOWN_KEY, String(untilMs));
  } else {
    localStorage.removeItem(REFRESH_COOLDOWN_KEY);
  }
}

function recordTransientRefreshFailure(error: unknown, refreshToken: string): void {
  if (trackedFailureRefreshToken !== refreshToken) {
    trackedFailureRefreshToken = refreshToken;
    consecutiveTransientFailures = 0;
  }
  consecutiveTransientFailures += 1;

  if (axios.isAxiosError(error) && error.response?.status === 429) {
    const headers = error.response.headers as Record<string, unknown> | undefined;
    const retryAfterRaw = headers?.['retry-after'] ?? headers?.['Retry-After'];
    const parsedMs = parseRetryAfterMs(retryAfterRaw);
    const fallbackBackoffMs = Math.min(60_000, 2_000 * 2 ** (consecutiveTransientFailures - 1));
    writeCooldownUntilMs(Date.now() + (parsedMs ?? fallbackBackoffMs));
    return;
  }

  if (consecutiveTransientFailures >= MAX_CONSECUTIVE_TRANSIENT_FAILURES) {
    writeCooldownUntilMs(Date.now() + DEFAULT_TRANSIENT_COOLDOWN_MS);
  }
}

function clearTransientRefreshFailures(): void {
  consecutiveTransientFailures = 0;
  trackedFailureRefreshToken = null;
  localStorage.removeItem(REFRESH_COOLDOWN_KEY);
}

/**
 * Khóa liên tab qua Web Locks API → đọc lại token mới nhất → gọi POST /auth/refresh nếu cần
 * → kiểm tra chống khôi phục phiên đã logout hoặc ghi đè tài khoản khác.
 */
async function doRefresh(
  failedAccessToken: string | null,
  refreshTokenBeforeLock: string | null,
): Promise<string> {
  return withCrossTabLock(REFRESH_LOCK_NAME, async () => {
    const latestRefreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
    if (!latestRefreshToken) {
      throw new NoRefreshTokenError();
    }

    const useAuthStore = await loadAuthStore();
    const storeBefore = useAuthStore.getState();

    if (storeBefore.isAuthenticated === false) {
      throw new StaleRefreshDiscardedError('logged_out');
    }

    const latestAccessToken = localStorage.getItem(ACCESS_TOKEN_KEY);
    const rotatedByAnotherTab =
      Boolean(latestAccessToken) &&
      ((Boolean(failedAccessToken) && latestAccessToken !== failedAccessToken) ||
        (Boolean(refreshTokenBeforeLock) && latestRefreshToken !== refreshTokenBeforeLock));

    if (rotatedByAnotherTab && latestAccessToken) {
      if (typeof storeBefore.tryApplyRefreshedTokens === 'function') {
        storeBefore.tryApplyRefreshedTokens({
          accessToken: latestAccessToken,
          refreshToken: latestRefreshToken,
          expectedRefreshToken: latestRefreshToken,
          expectedUserId: storeBefore.user?.id ?? null,
          expectedEpoch: storeBefore.sessionEpoch,
        });
      }
      return latestAccessToken;
    }

    if (trackedFailureRefreshToken !== latestRefreshToken) {
      trackedFailureRefreshToken = latestRefreshToken;
      consecutiveTransientFailures = 0;
    }

    const cooldownUntilMs = readCooldownUntilMs();
    const nowMs = Date.now();
    if (cooldownUntilMs > nowMs) {
      throw new RefreshCooldownError(cooldownUntilMs - nowMs);
    }

    if (consecutiveTransientFailures >= MAX_CONSECUTIVE_TRANSIENT_FAILURES) {
      consecutiveTransientFailures = 0;
    }

    const expectedUserId = storeBefore.user?.id;
    const expectedEpoch = storeBefore.sessionEpoch;
    const sentRefreshToken = latestRefreshToken;

    let data: RefreshResponse;
    try {
      const response = await axios.post<RefreshResponse>(
        `${API_BASE_URL}/auth/refresh`,
        { refreshToken: sentRefreshToken },
        { timeout: 15_000 },
      );
      data = response.data;
    } catch (err) {
      if (!shouldInvalidateSessionOnRefreshError(err)) {
        recordTransientRefreshFailure(err, sentRefreshToken);
      } else {
        clearTransientRefreshFailures();
      }
      throw err;
    }

    // Chống race condition: nếu trong lúc chờ mạng user đã logout hoặc đăng nhập tài khoản khác,
    // tuyệt đối không ghi đè token mới vào store/localStorage.
    const currentRefreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
    const storeAfter = useAuthStore.getState();

    if (!currentRefreshToken || storeAfter.isAuthenticated === false) {
      throw new StaleRefreshDiscardedError('logged_out');
    }

    if (
      currentRefreshToken !== sentRefreshToken ||
      (expectedUserId !== undefined && (storeAfter.user?.id ?? null) !== expectedUserId) ||
      (expectedEpoch !== undefined && storeAfter.sessionEpoch !== expectedEpoch)
    ) {
      throw new StaleRefreshDiscardedError('session_replaced');
    }

    if (typeof storeAfter.tryApplyRefreshedTokens === 'function') {
      const applied = storeAfter.tryApplyRefreshedTokens({
        accessToken: data.accessToken,
        refreshToken: data.refreshToken,
        expectedRefreshToken: sentRefreshToken,
        expectedUserId: expectedUserId ?? null,
        expectedEpoch,
      });
      if (!applied) {
        throw new StaleRefreshDiscardedError('session_replaced');
      }
    } else {
      storeAfter.setTokens({
        accessToken: data.accessToken,
        refreshToken: data.refreshToken,
      });
    }

    clearTransientRefreshFailures();
    return data.accessToken;
  });
}

/** Single-flight trong cùng tab kết hợp Web Locks liên tab. */
function requestNewAccessToken(
  failedAccessToken: string | null,
  refreshTokenBeforeLock: string | null,
): Promise<string> {
  refreshPromise ??= doRefresh(failedAccessToken, refreshTokenBeforeLock).finally(() => {
    refreshPromise = null;
  });
  return refreshPromise;
}

const REALTIME_TOKEN_REFRESH_LEEWAY_MS = 30_000;

function decodeJwtExpiryMs(token: string): number | null {
  const parts = token.split('.');
  if (parts.length !== 3 || !parts[1]) return null;
  try {
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, '=');
    const json = atob(padded);
    const parsed = JSON.parse(json) as { exp?: unknown };
    if (typeof parsed.exp === 'number' && Number.isFinite(parsed.exp)) {
      return parsed.exp * 1_000;
    }
    return null;
  } catch {
    return null;
  }
}

/**
 * Lấy access token mới nhất cho kết nối SignalR; tự động xoay vòng qua cơ chế
 * single-flight + Web Locks nếu token sắp/đã hết hạn và còn refresh token hợp lệ.
 */
export async function getValidAccessTokenForRealtime(): Promise<string> {
  if (typeof localStorage === 'undefined') return '';

  const currentAccessToken = localStorage.getItem(ACCESS_TOKEN_KEY);
  const currentRefreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);

  if (!currentAccessToken && !currentRefreshToken) {
    return '';
  }

  if (currentAccessToken) {
    const expMs = decodeJwtExpiryMs(currentAccessToken);
    if (expMs === null || expMs - Date.now() > REALTIME_TOKEN_REFRESH_LEEWAY_MS) {
      return currentAccessToken;
    }
  }

  if (!currentRefreshToken) {
    return currentAccessToken ?? '';
  }

  try {
    return await requestNewAccessToken(currentAccessToken, currentRefreshToken);
  } catch (err) {
    if (shouldInvalidateSessionOnRefreshError(err)) {
      const useAuthStore = await loadAuthStore();
      const latestState = useAuthStore.getState();
      if (latestState.isAuthenticated) {
        latestState.clearAuth();
      }
      return '';
    }
    return localStorage.getItem(ACCESS_TOKEN_KEY) ?? '';
  }
}

function isAuthEndpoint(url?: string): boolean {
  return !!url && /\/auth\/(login|register|refresh|verify-otp|resend-otp|logout)/.test(url);
}

function redirectToLoginPreservingUrl(): void {
  if (window.location.pathname !== '/login') {
    const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
    window.location.assign(`/login?returnUrl=${returnUrl}`);
  }
}

async function handleUnauthorized(error: AxiosError): Promise<unknown> {
  const config = error.config as RetriableConfig | undefined;
  const status = error.response?.status;
  const retryCount = config?._retryCount ?? 0;

  if (
    status !== 401 ||
    !config ||
    config._retried ||
    retryCount >= MAX_REQUEST_AUTH_RETRIES ||
    isAuthEndpoint(config.url)
  ) {
    return Promise.reject(error);
  }

  const failedAccessToken = extractBearerToken(config.headers?.Authorization);
  const refreshTokenBeforeLock = localStorage.getItem(REFRESH_TOKEN_KEY);

  if (!refreshTokenBeforeLock) {
    const useAuthStore = await loadAuthStore();
    const storeState = useAuthStore.getState();
    if (storeState.isAuthenticated === true || Boolean(failedAccessToken)) {
      storeState.clearAuth();
      redirectToLoginPreservingUrl();
    }
    return Promise.reject(error);
  }

  config._retried = true;
  config._retryCount = retryCount + 1;

  const tokenPromise = requestNewAccessToken(failedAccessToken, refreshTokenBeforeLock);
  const useAuthStore = await loadAuthStore();
  const initiatedUserId = useAuthStore.getState().user?.id;

  try {
    const newToken = await tokenPromise;

    // Đảm bảo phiên vẫn còn hợp lệ trước khi phát lại request gốc.
    const latestState = useAuthStore.getState();
    if (
      latestState.isAuthenticated === false ||
      (initiatedUserId !== undefined && (latestState.user?.id ?? null) !== initiatedUserId)
    ) {
      return Promise.reject(error);
    }

    config.headers.Authorization = `Bearer ${newToken}`;
    return apiClient(config);
  } catch (refreshError) {
    if (refreshError instanceof StaleRefreshDiscardedError) {
      return Promise.reject(refreshError);
    }

    if (shouldInvalidateSessionOnRefreshError(refreshError)) {
      const latestState = useAuthStore.getState();
      const currentRefreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
      const isSameOrEmptySession =
        !currentRefreshToken ||
        currentRefreshToken === refreshTokenBeforeLock ||
        (initiatedUserId !== undefined && (latestState.user?.id ?? null) === initiatedUserId);

      if (isSameOrEmptySession) {
        latestState.clearAuth();
        redirectToLoginPreservingUrl();
      }
    }

    return Promise.reject(refreshError);
  }
}

apiClient.interceptors.response.use((response) => response, handleUnauthorized);
