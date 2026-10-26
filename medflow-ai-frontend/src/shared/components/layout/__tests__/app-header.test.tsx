import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import type { UserDto } from '@/features/auth/types/auth.types';
import { AppHeader } from '../app-header';

const patient: UserDto = {
  id: 1,
  email: 'a@b.com',
  fullName: 'Nguyễn Văn A',
  roles: ['PATIENT'],
  patientCode: 'PAT-0001',
};

function renderHeader() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AppHeader />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AppHeader', () => {
  beforeEach(() => {
    localStorage.clear();
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });
  });

  it('guest → render PublicNavbar với nút Đăng nhập', () => {
    renderHeader();
    expect(screen.getByText('Đăng nhập')).toBeDefined();
  });

  it('đã login → render PatientNavbar với tên user, không có nút Đăng nhập', () => {
    useAuthStore.setState({ user: patient, isAuthenticated: true });
    renderHeader();
    expect(screen.queryByText('Đăng nhập')).toBeNull();
    expect(screen.getAllByText('Nguyễn Văn A').length).toBeGreaterThan(0);
  });
});
