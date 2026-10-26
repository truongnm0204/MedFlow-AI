import { beforeEach, describe, expect, it } from 'vitest';

import { useAuthStore } from '../store/auth.store';
import type { UserDto } from '../types/auth.types';

const mockUser: UserDto = {
  id: 1,
  email: 'test@example.com',
  fullName: 'Nguyễn Văn Test',
  roles: ['PATIENT'],
  patientCode: 'PAT-0001',
};

describe('auth.store', () => {
  beforeEach(() => {
    localStorage.clear();
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });
  });

  it('setAuth lưu user + tokens + đánh dấu authenticated', () => {
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
    expect(localStorage.getItem('medflow.access_token')).toBe('access-1');
    expect(localStorage.getItem('medflow.refresh_token')).toBe('refresh-1');
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
    expect(localStorage.getItem('medflow.access_token')).toBeNull();
    expect(localStorage.getItem('medflow.refresh_token')).toBeNull();
  });
});
