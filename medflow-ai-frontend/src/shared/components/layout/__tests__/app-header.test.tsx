import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import * as authApi from '@/features/auth/api/auth.api';
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

  it('staff user (DOCTOR) → hiển thị tên bác sĩ, nhãn role, mã NV và link Bàn làm việc trong UserMenu', () => {
    vi.spyOn(authApi, 'getMe').mockResolvedValue({
      id: 10,
      email: 'doctor01@medflow.test',
      fullName: 'BS Nguyễn Minh Đức',
      roles: ['DOCTOR'],
      patientCode: null,
      staffCode: 'STF-001',
    });

    useAuthStore.setState({
      user: {
        id: 10,
        email: 'doctor01@medflow.test',
        fullName: 'BS Nguyễn Minh Đức',
        roles: ['DOCTOR'],
        patientCode: null,
        staffCode: 'STF-001',
      },
      isAuthenticated: true,
    });

    renderHeader();
    expect(screen.getAllByText('BS Nguyễn Minh Đức').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByLabelText('Menu tài khoản'));
    expect(screen.getByText('Bác sĩ lâm sàng')).toBeDefined();
    expect(screen.getByText('Mã NV: STF-001')).toBeDefined();

    const workbenchLink = screen.getByRole('link', { name: /Bàn làm việc \(Bác sĩ lâm sàng\)/i });
    expect(workbenchLink.getAttribute('href')).toBe('/doctor');
  });

  it('fallback email prefix khi fullName là null và đồng bộ useAuthStore.user khi useMe() trả về', async () => {
    const refreshedProfile: UserDto = {
      id: 11,
      email: 'doctor01@medflow.test',
      fullName: 'BS Trần Quốc Bảo',
      roles: ['DOCTOR'],
      patientCode: null,
      staffCode: 'STF-002',
    };
    vi.spyOn(authApi, 'getMe').mockResolvedValue(refreshedProfile);

    useAuthStore.setState({
      user: {
        id: 11,
        email: 'doctor01@medflow.test',
        fullName: null,
        roles: ['DOCTOR'],
        patientCode: null,
        staffCode: null,
      },
      isAuthenticated: true,
    });

    renderHeader();
    // Trước khi getMe resolve: hiển thị prefix email 'doctor01', không hiển thị 'Khách'
    expect(screen.queryByText('Khách')).toBeNull();
    expect(screen.getAllByText('doctor01').length).toBeGreaterThan(0);

    // Sau khi useMe() resolve: store và header tự động cập nhật sang tên thật từ server
    await waitFor(() => {
      expect(useAuthStore.getState().user?.fullName).toBe('BS Trần Quốc Bảo');
      expect(useAuthStore.getState().user?.staffCode).toBe('STF-002');
    });
    expect(screen.getAllByText('BS Trần Quốc Bảo').length).toBeGreaterThan(0);
  });
});
