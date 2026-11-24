import { beforeEach, describe, expect, it, vi } from 'vitest';

import { queryClient } from '@/shared/lib/query-client';

import {
  ACCESS_TOKEN_KEY,
  AUTH_STORAGE_KEY,
  REFRESH_TOKEN_KEY,
  useAuthStore,
} from '../store/auth.store';
import type { UserDto } from '../types/auth.types';

const mockUser: UserDto = {
  id: 1,
  email: 'test@example.com',
  fullName: 'Nguyễn Văn Test',
  roles: ['PATIENT'],
  patientCode: 'PAT-0001',
};

describe('auth.store & multi-tab synchronization', () => {
  beforeEach(() => {
    localStorage.clear();
    queryClient.clear();
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
      sessionEpoch: 0,
    });
  });

  it('setAuth lưu user + tokens + đánh dấu authenticated + tăng sessionEpoch', () => {
    useAuthStore.getState().setAuth({
      user: mockUser,
      accessToken: 'access-1',
      refreshToken: 'refresh-1',
    });

    const s = useAuthStore.getState();
    expect(s.user).toEqual(mockUser);
    expect(s.accessToken).toBe('access-1');
    expect(s.refreshToken).toBe('refresh-1');
    expect(s.isAuthenticated).toBe(true);
    expect(s.sessionEpoch).toBe(1);
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe('access-1');
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-1');
  });

  it('setTokens cập nhật cặp token, giữ nguyên user', () => {
    useAuthStore.getState().setAuth({
      user: mockUser,
      accessToken: 'old-a',
      refreshToken: 'old-r',
    });
    useAuthStore.getState().setTokens({ accessToken: 'new-a', refreshToken: 'new-r' });

    const s = useAuthStore.getState();
    expect(s.accessToken).toBe('new-a');
    expect(s.refreshToken).toBe('new-r');
    expect(s.user).toEqual(mockUser);
  });

  it('setUser cập nhật thông tin user (bao gồm staffCode và fullName) và giữ nguyên tokens', () => {
    useAuthStore.getState().setAuth({
      user: mockUser,
      accessToken: 'token-a',
      refreshToken: 'token-r',
    });

    const updatedStaffUser: UserDto = {
      id: 2,
      email: 'doctor01@medflow.test',
      fullName: 'BS Nguyễn Minh Đức',
      roles: ['DOCTOR'],
      patientCode: null,
      staffCode: 'STF-001',
    };

    useAuthStore.getState().setUser(updatedStaffUser);

    const s = useAuthStore.getState();
    expect(s.user).toEqual(updatedStaffUser);
    expect(s.accessToken).toBe('token-a');
    expect(s.refreshToken).toBe('token-r');
    expect(s.isAuthenticated).toBe(true);
  });

  it('clearAuth xoá state + localStorage', () => {
    useAuthStore.getState().setAuth({
      user: mockUser,
      accessToken: 'a',
      refreshToken: 'r',
    });
    useAuthStore.getState().clearAuth();

    const s = useAuthStore.getState();
    expect(s.user).toBeNull();
    expect(s.accessToken).toBeNull();
    expect(s.refreshToken).toBeNull();
    expect(s.isAuthenticated).toBe(false);
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });

  it('tryApplyRefreshedTokens chặn ghi đè khi đã logout hoặc đổi tài khoản / epoch', () => {
    useAuthStore.getState().setAuth({
      user: mockUser,
      accessToken: 'a-1',
      refreshToken: 'r-1',
    });
    const epoch1 = useAuthStore.getState().sessionEpoch;

    // Sai expectedRefreshToken → từ chối.
    expect(
      useAuthStore.getState().tryApplyRefreshedTokens({
        accessToken: 'a-2',
        refreshToken: 'r-2',
        expectedRefreshToken: 'wrong-r',
        expectedUserId: mockUser.id,
        expectedEpoch: epoch1,
      }),
    ).toBe(false);
    expect(useAuthStore.getState().accessToken).toBe('a-1');

    // Khớp toàn bộ điều kiện → áp dụng thành công.
    expect(
      useAuthStore.getState().tryApplyRefreshedTokens({
        accessToken: 'a-2',
        refreshToken: 'r-2',
        expectedRefreshToken: 'r-1',
        expectedUserId: mockUser.id,
        expectedEpoch: epoch1,
      }),
    ).toBe(true);
    expect(useAuthStore.getState().accessToken).toBe('a-2');
    expect(useAuthStore.getState().refreshToken).toBe('r-2');

    // Đã clearAuth → response đến muộn bị từ chối, không khôi phục phiên.
    useAuthStore.getState().clearAuth();
    expect(
      useAuthStore.getState().tryApplyRefreshedTokens({
        accessToken: 'a-3',
        refreshToken: 'r-3',
        expectedRefreshToken: 'r-2',
        expectedUserId: mockUser.id,
        expectedEpoch: epoch1,
      }),
    ).toBe(false);
    expect(useAuthStore.getState().isAuthenticated).toBe(false);
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
  });

  it('đồng bộ Login & Refresh từ tab khác qua storage event mà không gây vòng lặp ghi ngược localStorage', () => {
    const otherTabUser: UserDto = {
      id: 9,
      email: 'reception01@medflow.test',
      fullName: 'Lễ tân Lê Lan',
      roles: ['RECEPTION'],
      patientCode: null,
      staffCode: 'STF-003',
    };

    localStorage.setItem(ACCESS_TOKEN_KEY, 'tab-b-access-1');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'tab-b-refresh-1');
    localStorage.setItem(
      AUTH_STORAGE_KEY,
      JSON.stringify({
        state: {
          user: otherTabUser,
          accessToken: 'tab-b-access-1',
          refreshToken: 'tab-b-refresh-1',
          sessionEpoch: 5,
        },
        version: 0,
      }),
    );

    const setItemSpy = vi.spyOn(Storage.prototype, 'setItem');
    window.dispatchEvent(new StorageEvent('storage', { key: AUTH_STORAGE_KEY }));

    const s1 = useAuthStore.getState();
    expect(s1.isAuthenticated).toBe(true);
    expect(s1.user).toEqual(otherTabUser);
    expect(s1.accessToken).toBe('tab-b-access-1');
    expect(s1.refreshToken).toBe('tab-b-refresh-1');
    expect(s1.sessionEpoch).toBe(5);
    expect(setItemSpy).not.toHaveBeenCalled();

    // Tab B tiếp tục silent refresh sang cặp token mới.
    setItemSpy.mockRestore();
    localStorage.setItem(ACCESS_TOKEN_KEY, 'tab-b-access-2');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'tab-b-refresh-2');
    window.dispatchEvent(new StorageEvent('storage', { key: ACCESS_TOKEN_KEY }));

    const s2 = useAuthStore.getState();
    expect(s2.accessToken).toBe('tab-b-access-2');
    expect(s2.refreshToken).toBe('tab-b-refresh-2');
    expect(s2.user).toEqual(otherTabUser);
  });

  it('đồng bộ Logout từ tab khác qua storage event và xoá sạch React Query PHI cache', () => {
    useAuthStore.getState().setAuth({
      user: mockUser,
      accessToken: 'a-live',
      refreshToken: 'r-live',
    });
    queryClient.setQueryData(['patients', 'me', 'visits'], [{ visitCode: 'VIS-PHI-01' }]);
    const clearCacheSpy = vi.spyOn(queryClient, 'clear');

    // Giả lập Tab A xoá token trong localStorage.
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.setItem(
      AUTH_STORAGE_KEY,
      JSON.stringify({
        state: { user: null, accessToken: null, refreshToken: null, sessionEpoch: 2 },
        version: 0,
      }),
    );

    window.dispatchEvent(new StorageEvent('storage', { key: ACCESS_TOKEN_KEY }));

    const s = useAuthStore.getState();
    expect(s.isAuthenticated).toBe(false);
    expect(s.user).toBeNull();
    expect(s.accessToken).toBeNull();
    expect(s.refreshToken).toBeNull();
    expect(clearCacheSpy).toHaveBeenCalledTimes(1);
    expect(queryClient.getQueryData(['patients', 'me', 'visits'])).toBeUndefined();
    clearCacheSpy.mockRestore();
  });
});
