import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';

export const apiClient = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
  timeout: 15_000,
});

// Attach auth token when present.
apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('medflow.access_token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// --- 401 → silent refresh (single-flight) ---------------------------------

interface RefreshResponse {
  accessToken: string;
  accessTokenExpiresIn: number;
  refreshToken: string;
  refreshTokenExpiresIn: number;
}

type RetriableConfig = InternalAxiosRequestConfig & { _retried?: boolean };

let refreshPromise: Promise<string> | null = null;

/** Gọi POST /auth/refresh bằng axios raw (không qua interceptor). */
async function doRefresh(): Promise<string> {
  const refreshToken = localStorage.getItem('medflow.refresh_token');
  if (!refreshToken) throw new Error('No refresh token');

  const { data } = await axios.post<RefreshResponse>(
    '/api/v1/auth/refresh',
    { refreshToken },
    { timeout: 15_000 },
  );

  // Dynamic import tránh circular dependency axios ↔ store.
  const { useAuthStore } = await import('@/features/auth/store/auth.store');
  useAuthStore.getState().setTokens({
    accessToken: data.accessToken,
    refreshToken: data.refreshToken,
  });
  return data.accessToken;
}

/** Single-flight: các request 401 đồng thời chỉ gọi refresh 1 lần. */
function requestNewAccessToken(): Promise<string> {
  refreshPromise ??= doRefresh().finally(() => {
    refreshPromise = null;
  });
  return refreshPromise;
}

function isAuthEndpoint(url?: string): boolean {
  return !!url && /\/auth\/(login|register|refresh|verify-otp|resend-otp)/.test(url);
}

async function handleUnauthorized(error: AxiosError): Promise<unknown> {
  const config = error.config as RetriableConfig | undefined;
  const status = error.response?.status;

  if (status !== 401 || !config || config._retried || isAuthEndpoint(config.url)) {
    return Promise.reject(error);
  }
  config._retried = true;

  try {
    const newToken = await requestNewAccessToken();
    config.headers.Authorization = `Bearer ${newToken}`;
    return apiClient(config);
  } catch (refreshError) {
    // Refresh fail (reuse detected / hết hạn / mất token) → logout sạch.
    const { useAuthStore } = await import('@/features/auth/store/auth.store');
    useAuthStore.getState().clearAuth();
    if (window.location.pathname !== '/login') {
      const returnUrl = encodeURIComponent(window.location.pathname);
      window.location.assign(`/login?returnUrl=${returnUrl}`);
    }
    return Promise.reject(refreshError);
  }
}

apiClient.interceptors.response.use((response) => response, handleUnauthorized);
