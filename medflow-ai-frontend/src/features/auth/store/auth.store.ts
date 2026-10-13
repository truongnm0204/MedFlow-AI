import { create } from 'zustand';
import { persist } from 'zustand/middleware';

import type { UserDto } from '../types/auth.types';

export const ACCESS_TOKEN_KEY = 'medflow.access_token';
export const REFRESH_TOKEN_KEY = 'medflow.refresh_token';

interface AuthState {
  user: UserDto | null;
  accessToken: string | null;
  refreshToken: string | null;
  isAuthenticated: boolean;
  setAuth: (payload: { user: UserDto; accessToken: string; refreshToken: string }) => void;
  setTokens: (payload: { accessToken: string; refreshToken: string }) => void;
  clearAuth: () => void;
}

// Đồng bộ localStorage để axios interceptor đọc (tránh circular import axios ↔ store).
function syncTokenStorage(accessToken: string | null, refreshToken: string | null) {
  if (accessToken) localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
  else localStorage.removeItem(ACCESS_TOKEN_KEY);
  if (refreshToken) localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
  else localStorage.removeItem(REFRESH_TOKEN_KEY);
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,

      setAuth: ({ user, accessToken, refreshToken }) => {
        syncTokenStorage(accessToken, refreshToken);
        set({ user, accessToken, refreshToken, isAuthenticated: true });
      },

      setTokens: ({ accessToken, refreshToken }) => {
        syncTokenStorage(accessToken, refreshToken);
        set({ accessToken, refreshToken });
      },

      clearAuth: () => {
        syncTokenStorage(null, null);
        set({ user: null, accessToken: null, refreshToken: null, isAuthenticated: false });
      },
    }),
    {
      name: 'medflow.auth',
      partialize: (state) => ({
        user: state.user,
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
      }),
      // Rehydrate: isAuthenticated suy ra từ token tồn tại.
      merge: (persisted, current) => {
        const p = persisted as Partial<AuthState> | undefined;
        return {
          ...current,
          ...p,
          isAuthenticated: !!p?.accessToken,
        };
      },
    },
  ),
);
