import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import * as authApi from '../api/auth.api';
import { ForgotPasswordPage } from '../pages/forgot-password-page';
import { LoginPage } from '../pages/login-page';

vi.mock('../api/auth.api', () => ({
  login: vi.fn(),
  register: vi.fn(),
  verifyOtp: vi.fn(),
  resendOtp: vi.fn(),
  forgotPassword: vi.fn(),
  resetPassword: vi.fn(),
  changePassword: vi.fn(),
  logout: vi.fn(),
  getMe: vi.fn(),
}));

function renderAuthRoutes(initialEntry: string) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/forgot-password" element={<ForgotPasswordPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ForgotPasswordPage & LoginPage recovery link', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('dẫn hướng từ link Quên mật khẩu? trên LoginPage sang /forgot-password', async () => {
    renderAuthRoutes('/login');

    const forgotLink = screen.getByRole('link', { name: 'Quên mật khẩu?' });
    expect(forgotLink).toHaveAttribute('href', '/forgot-password');

    fireEvent.click(forgotLink);
    expect(await screen.findByRole('heading', { name: 'Quên mật khẩu?' })).toBeInTheDocument();
  });

  it('hoàn tất luồng 2 bước gửi OTP khôi phục -> đặt lại mật khẩu mới -> điều hướng /login?reset=1', async () => {
    const antiEnumMsg =
      'Nếu email đã được đăng ký và đủ điều kiện, mã OTP khôi phục mật khẩu sẽ được gửi đến email của bạn.';
    vi.mocked(authApi.forgotPassword).mockResolvedValue({ message: antiEnumMsg });
    vi.mocked(authApi.resetPassword).mockResolvedValue({
      message: 'Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.',
    });

    renderAuthRoutes('/forgot-password');

    fireEvent.change(screen.getByLabelText('Email đăng ký'), {
      target: { value: 'patient01@medflow.test' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi mã OTP khôi phục' }));

    expect(await screen.findByText(antiEnumMsg)).toBeInTheDocument();
    expect(authApi.forgotPassword).toHaveBeenCalledWith({ email: 'patient01@medflow.test' });

    const otpInputs = screen.getAllByRole('textbox');
    '654321'.split('').forEach((digit, idx) => {
      fireEvent.change(otpInputs[idx], { target: { value: digit } });
    });

    fireEvent.change(screen.getByLabelText('Mật khẩu mới'), {
      target: { value: 'StrongNewPass123' },
    });
    fireEvent.change(screen.getByLabelText('Xác nhận mật khẩu mới'), {
      target: { value: 'StrongNewPass123' },
    });

    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận đặt lại mật khẩu' }));

    await waitFor(() => {
      expect(authApi.resetPassword).toHaveBeenCalledWith({
        email: 'patient01@medflow.test',
        otpCode: '654321',
        newPassword: 'StrongNewPass123',
      });
    });

    expect(
      await screen.findByText('Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.'),
    ).toBeInTheDocument();
  });
});
