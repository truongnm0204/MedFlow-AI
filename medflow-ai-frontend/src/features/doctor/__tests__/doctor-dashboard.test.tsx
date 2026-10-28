import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { DoctorDashboardPage } from '../pages/doctor-dashboard-page';

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <DoctorDashboardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DoctorDashboardPage', () => {
  it('renders doctor header, identity, and 4 clinical stat cards', () => {
    useAuthStore.setState({
      user: {
        id: 12,
        email: 'bacsi.hung@medflow.test',
        fullName: 'BS. Nguyễn Văn Hùng',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    expect(screen.getByText('Tổng quan bàn khám bác sĩ')).toBeInTheDocument();
    expect(screen.getAllByText('BS. Nguyễn Văn Hùng').length).toBeGreaterThan(0);
    expect(screen.getByText('Chờ khám tại phòng')).toBeInTheDocument();
    expect(screen.getByText('Đang khám lâm sàng')).toBeInTheDocument();
    expect(screen.getByText('Chờ duyệt kết quả CLS')).toBeInTheDocument();
    expect(screen.getByText('Hoàn tất trong ca')).toBeInTheDocument();
  });

  it('renders assigned patient queue and pending diagnostic results', () => {
    useAuthStore.setState({
      user: null,
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    expect(screen.getByText('Hàng đợi bệnh nhân được phân công')).toBeInTheDocument();
    expect(screen.getByText('B008')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText('Điện tâm đồ 12 chuyển đạo (ECG)')).toBeInTheDocument();
  });

  it('filters assigned queue by status and search keyword and shows empty state when unmatched', () => {
    useAuthStore.setState({
      user: null,
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Chờ CLS' }));
    expect(screen.getByText('Võ Minh Tuấn')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Tất cả' }));
    const searchInput = screen.getByLabelText('Tìm bệnh nhân trong hàng đợi');
    fireEvent.change(searchInput, { target: { value: 'Trần Thị Mai' } });

    expect(screen.getByText('Trần Thị Mai')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();

    fireEvent.change(searchInput, { target: { value: 'KHONG_TON_TAI_999' } });
    expect(screen.getByText('Không có bệnh nhân phù hợp bộ lọc hiện tại.')).toBeInTheDocument();
  });

  it('exposes only Doctor-authorized entrypoints and hides reception/admin actions', () => {
    useAuthStore.setState({
      user: {
        id: 12,
        email: 'bacsi.hung@medflow.test',
        fullName: 'BS. Nguyễn Văn Hùng',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    // Lối vào nghiệp vụ của Doctor
    expect(screen.getByText('Gọi bệnh nhân tiếp theo')).toBeInTheDocument();
    expect(screen.getByText('Mở phiếu khám lâm sàng')).toBeInTheDocument();
    expect(screen.getByText('Chỉ định cận lâm sàng')).toBeInTheDocument();
    expect(screen.getAllByText('Kê đơn thuốc điện tử').length).toBeGreaterThan(0);

    // Không hiển thị nghiệp vụ của Lễ tân / Admin
    expect(screen.queryByText('Check-in & Tiếp nhận')).not.toBeInTheDocument();
    expect(screen.queryByText('Quản lý tài khoản nhân viên')).not.toBeInTheDocument();
  });
});
