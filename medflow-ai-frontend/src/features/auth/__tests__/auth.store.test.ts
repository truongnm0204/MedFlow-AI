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
    expect(localStorage.getItem('medflow.access_token')).toBeNull();
    expect(localStorage.getItem('medflow.refresh_token')).toBeNull();
  });
});
