import { create } from 'zustand';
import { createJSONStorage, persist, type StateStorage } from 'zustand/middleware';

import {
  ACCESS_TOKEN_KEY,
  AUTH_STORAGE_KEY,
  AUTH_SYNC_CHANNEL,
  REFRESH_COOLDOWN_KEY,
  REFRESH_TOKEN_KEY,
} from '@/shared/lib/auth-storage';
import { clearAllAdminDrafts } from '@/shared/lib/admin-draft-storage';
import { notifyAuthSessionInvalidated } from '@/shared/lib/navigation-guard-manager';
import { queryClient } from '@/shared/lib/query-client';

import type { UserDto } from '../types/auth.types';

export {
  ACCESS_TOKEN_KEY,
  AUTH_STORAGE_KEY,
  AUTH_SYNC_CHANNEL,
  REFRESH_COOLDOWN_KEY,
  REFRESH_TOKEN_KEY,
};

export interface ApplyRefreshedTokensPayload {
  accessToken: string;
  refreshToken: string;
  expectedRefreshToken?: string;
  expectedUserId?: number | null;
  expectedEpoch?: number;
}

interface AuthState {
  user: UserDto | null;
  accessToken: string | null;
  refreshToken: string | null;
  isAuthenticated: boolean;
  sessionEpoch: number;
  setAuth: (payload: { user: UserDto; accessToken: string; refreshToken: string }) => void;
  setUser: (user: UserDto) => void;
  setTokens: (payload: { accessToken: string; refreshToken: string }) => void;
  tryApplyRefreshedTokens: (payload: ApplyRefreshedTokensPayload) => boolean;
  clearAuth: () => void;
}

interface AuthBroadcastMessage {
  senderTabId: string;
  type: 'AUTH_UPDATED' | 'AUTH_CLEARED';
}

interface PersistedAuthEnvelope {
  state?: Partial<Pick<AuthState, 'user' | 'accessToken' | 'refreshToken' | 'sessionEpoch'>>;
}

const TAB_ID =
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`;

let isApplyingExternalSync = false;
let broadcastChannel: BroadcastChannel | null = null;

// Đồng bộ localStorage để axios interceptor đọc (tránh circular import axios ↔ store).
function syncTokenStorage(accessToken: string | null, refreshToken: string | null) {
  if (typeof localStorage === 'undefined') return;
  if (accessToken) localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
  else localStorage.removeItem(ACCESS_TOKEN_KEY);
  if (refreshToken) localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
  else localStorage.removeItem(REFRESH_TOKEN_KEY);
}

function clearRefreshCooldownStorage() {
  if (typeof localStorage === 'undefined') return;
  localStorage.removeItem(REFRESH_COOLDOWN_KEY);
}

const crossTabSafeStorage: StateStorage = {
  getItem: (name) => (typeof localStorage !== 'undefined' ? localStorage.getItem(name) : null),
  setItem: (name, value) => {
    if (isApplyingExternalSync || typeof localStorage === 'undefined') return;
    localStorage.setItem(name, value);
  },
  removeItem: (name) => {
    if (isApplyingExternalSync || typeof localStorage === 'undefined') return;
    localStorage.removeItem(name);
  },
};

function notifyOtherTabs(type: AuthBroadcastMessage['type']) {
  if (isApplyingExternalSync || !broadcastChannel) return;
  try {
    const message: AuthBroadcastMessage = { senderTabId: TAB_ID, type };
    broadcastChannel.postMessage(message);
  } catch {
    // Bỏ qua nếu BroadcastChannel đã đóng trong môi trường test/teardown.
  }
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
      sessionEpoch: 0,

      setAuth: ({ user, accessToken, refreshToken }) => {
        clearRefreshCooldownStorage();
        syncTokenStorage(accessToken, refreshToken);
        const nextEpoch = get().sessionEpoch + 1;
        set({
          user,
          accessToken,
          refreshToken,
          isAuthenticated: true,
          sessionEpoch: nextEpoch,
        });
        notifyOtherTabs('AUTH_UPDATED');
      },

      setUser: (user) => {
        set({ user });
        notifyOtherTabs('AUTH_UPDATED');
      },

      setTokens: ({ accessToken, refreshToken }) => {
        syncTokenStorage(accessToken, refreshToken);
        set({ accessToken, refreshToken });
        notifyOtherTabs('AUTH_UPDATED');
      },

      tryApplyRefreshedTokens: ({
        accessToken,
        refreshToken,
        expectedRefreshToken,
        expectedUserId,
        expectedEpoch,
      }) => {
        const current = get();
        if (!current.isAuthenticated) {
          return false;
        }
        if (
          expectedRefreshToken !== undefined &&
          typeof localStorage !== 'undefined' &&
          localStorage.getItem(REFRESH_TOKEN_KEY) !== expectedRefreshToken
        ) {
          return false;
        }
        if (
          expectedRefreshToken !== undefined &&
          current.refreshToken !== null &&
          current.refreshToken !== expectedRefreshToken
        ) {
          return false;
        }
        if (expectedUserId !== undefined && (current.user?.id ?? null) !== expectedUserId) {
          return false;
        }
        if (expectedEpoch !== undefined && current.sessionEpoch !== expectedEpoch) {
          return false;
        }

        clearRefreshCooldownStorage();
        syncTokenStorage(accessToken, refreshToken);
        set({ accessToken, refreshToken });
        notifyOtherTabs('AUTH_UPDATED');
        return true;
      },

      clearAuth: () => {
        notifyAuthSessionInvalidated();
        clearAllAdminDrafts();
        clearRefreshCooldownStorage();
        syncTokenStorage(null, null);
        const nextEpoch = get().sessionEpoch + 1;
        set({
          user: null,
          accessToken: null,
          refreshToken: null,
          isAuthenticated: false,
          sessionEpoch: nextEpoch,
        });
        notifyOtherTabs('AUTH_CLEARED');
      },
    }),
    {
      name: AUTH_STORAGE_KEY,
      storage: createJSONStorage(() => crossTabSafeStorage),
      partialize: (state) => ({
        user: state.user,
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
        sessionEpoch: state.sessionEpoch,
      }),
      // Rehydrate: isAuthenticated suy ra từ token tồn tại.
      merge: (persisted, current) => {
        const p = persisted as Partial<AuthState> | undefined;
        return {
          ...current,
          ...p,
          sessionEpoch: typeof p?.sessionEpoch === 'number' ? p.sessionEpoch : current.sessionEpoch,
          isAuthenticated: !!p?.accessToken,
        };
      },
    },
  ),
);

function parsePersistedSnapshot(): Partial<
  Pick<AuthState, 'user' | 'accessToken' | 'refreshToken' | 'sessionEpoch'>
> | null {
  if (typeof localStorage === 'undefined') return null;
  const raw = localStorage.getItem(AUTH_STORAGE_KEY);
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as PersistedAuthEnvelope;
    return parsed.state ?? null;
  } catch {
    return null;
  }
}

function isSameUser(a: UserDto | null, b: UserDto | null): boolean {
  if (a === b) return true;
  if (!a || !b) return false;
  return JSON.stringify(a) === JSON.stringify(b);
}

/**
 * Đồng bộ thụ động trạng thái auth từ `localStorage` sang bộ nhớ của tab hiện tại
 * mà không ghi ngược `localStorage` hay phát lại sự kiện sang các tab khác.
 */
export function syncAuthStateFromStorage(): void {
  if (typeof localStorage === 'undefined') return;

  const snapshot = parsePersistedSnapshot();
  const lsAccess = localStorage.getItem(ACCESS_TOKEN_KEY) ?? snapshot?.accessToken ?? null;
  const lsRefresh = localStorage.getItem(REFRESH_TOKEN_KEY) ?? snapshot?.refreshToken ?? null;
  const current = useAuthStore.getState();

  // Trường hợp tab khác đã logout / xoá phiên: xoá state và dọn sạch PHI cache trên tab này.
  if (!lsAccess || !lsRefresh) {
    const hadSession =
      current.isAuthenticated ||
      current.accessToken !== null ||
      current.refreshToken !== null ||
      current.user !== null;

    if (!hadSession) return;

    isApplyingExternalSync = true;
    try {
      notifyAuthSessionInvalidated();
      clearAllAdminDrafts();
      useAuthStore.setState({
        user: null,
        accessToken: null,
        refreshToken: null,
        isAuthenticated: false,
        sessionEpoch:
          typeof snapshot?.sessionEpoch === 'number' && snapshot.sessionEpoch > current.sessionEpoch
            ? snapshot.sessionEpoch
            : current.sessionEpoch + 1,
      });
    } finally {
      isApplyingExternalSync = false;
    }

    queryClient.clear();
    return;
  }

  const nextUser = snapshot?.user ?? current.user;
  const nextEpoch =
    typeof snapshot?.sessionEpoch === 'number' ? snapshot.sessionEpoch : current.sessionEpoch;

  const unchanged =
    current.isAuthenticated &&
    current.accessToken === lsAccess &&
    current.refreshToken === lsRefresh &&
    current.sessionEpoch === nextEpoch &&
    isSameUser(current.user, nextUser ?? null);

  if (unchanged) return;

  isApplyingExternalSync = true;
  try {
    useAuthStore.setState({
      user: nextUser ?? null,
      accessToken: lsAccess,
      refreshToken: lsRefresh,
      isAuthenticated: true,
      sessionEpoch: nextEpoch,
    });
  } finally {
    isApplyingExternalSync = false;
  }
}

function setupCrossTabListeners() {
  if (typeof window === 'undefined') return;

  window.addEventListener('storage', (event: StorageEvent) => {
    if (
      event.key === null ||
      event.key === AUTH_STORAGE_KEY ||
      event.key === ACCESS_TOKEN_KEY ||
      event.key === REFRESH_TOKEN_KEY
    ) {
      syncAuthStateFromStorage();
    }
  });

  if (typeof BroadcastChannel !== 'undefined') {
    try {
      broadcastChannel = new BroadcastChannel(AUTH_SYNC_CHANNEL);
      broadcastChannel.onmessage = (event: MessageEvent<AuthBroadcastMessage>) => {
        const msg = event.data;
        if (!msg || msg.senderTabId === TAB_ID) return;
        syncAuthStateFromStorage();
      };
    } catch {
      broadcastChannel = null;
    }
  }
}

setupCrossTabListeners();
