import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import * as authApi from '@/features/auth/api/auth.api';
import { useAuthStore } from '@/features/auth/store/auth.store';

import { AccountSettingsPage } from '../pages/account-settings-page';

vi.mock('@/features/auth/api/auth.api', () => ({
  getMe: vi.fn(),
  logout: vi.fn(),
}));

function renderSettingsPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/settings']}>
        <Routes>
          <Route path="/" element={<div>Trang chủ Public</div>} />
          <Route path="/settings" element={<AccountSettingsPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AccountSettingsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('displays patient security info, PHI shortcuts, and logs out current session', async () => {
    const patientUser = {
      id: 1,
      email: 'patient@medflow.vn',
      fullName: 'Nguyễn Văn An',
      roles: ['PATIENT'],
      patientCode: 'PAT-001',
    };
    useAuthStore.setState({
      user: patientUser,
      accessToken: 'access-1',
      refreshToken: 'refresh-1',
      isAuthenticated: true,
      sessionEpoch: 3,
    });
    vi.mocked(authApi.getMe).mockResolvedValue(patientUser);
    vi.mocked(authApi.logout).mockResolvedValue();

    renderSettingsPage();

    expect(screen.getByText('Cài đặt tài khoản & Bảo mật phiên')).toBeInTheDocument();
    expect(screen.getByText('PAT-001')).toBeInTheDocument();
    expect(screen.getByText(/EPOCH #3/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Hồ sơ gia đình & Xác minh PHI/i })).toHaveAttribute(
      'href',
      '/family',
    );

    fireEvent.click(screen.getByRole('button', { name: /Đăng xuất phiên hiện tại/i }));

    await waitFor(() => {
      expect(authApi.logout).toHaveBeenCalledWith({ refreshToken: 'refresh-1' });
      expect(useAuthStore.getState().isAuthenticated).toBe(false);
    });
  });

  it('hides patient-only PHI shortcuts for staff-only accounts and shows workbench shortcut', async () => {
    const doctorUser = {
      id: 2,
      email: 'doctor@medflow.vn',
      fullName: 'BS. Lê Minh',
      roles: ['DOCTOR'],
      patientCode: null,
      staffCode: 'DOC-02',
    };
    useAuthStore.setState({
      user: doctorUser,
      accessToken: 'access-doc',
      refreshToken: 'refresh-doc',
      isAuthenticated: true,
      sessionEpoch: 1,
    });
    vi.mocked(authApi.getMe).mockResolvedValue(doctorUser);

    renderSettingsPage();

    expect(screen.getByText('DOC-02')).toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: /Hồ sơ gia đình & Xác minh PHI/i }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Mở Bàn làm việc/i })).toBeInTheDocument();
  });
});
