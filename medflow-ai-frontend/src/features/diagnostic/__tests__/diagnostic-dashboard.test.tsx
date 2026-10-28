import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';

import { DiagnosticDashboardPage } from '../pages/diagnostic-dashboard-page';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <DiagnosticDashboardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DiagnosticDashboardPage', () => {
  it('render dashboard header, sidebar dung vai tro CLS va 4 stat cards', () => {
    useAuthStore.setState({
      user: {
        id: 8,
        email: 'ktv.cls@medflow.test',
        fullName: 'Phan Kỹ Thuật',
        roles: ['DIAGNOSTIC_STAFF'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    // Label "Tổng quan cận lâm sàng" xuất hiện ở cả sidebar nav và page header.
    expect(screen.getAllByText('Tổng quan cận lâm sàng').length).toBeGreaterThan(0);
    expect(screen.getByText('Chờ xác nhận chỉ định')).toBeInTheDocument();
    expect(screen.getByText('Hàng đợi thực hiện')).toBeInTheDocument();
    expect(screen.getByText('Đang thực hiện CLS')).toBeInTheDocument();
    expect(screen.getByText('Đã trả kết quả hôm nay')).toBeInTheDocument();

    // Lối vào chức năng được phép của Diagnostic Services Staff:
    expect(screen.getByText('Chỉ định chờ xác nhận')).toBeInTheDocument();
    expect(screen.getByText('Hàng đợi thực hiện CLS')).toBeInTheDocument();
    expect(screen.getByText('Thực hiện kỹ thuật')).toBeInTheDocument();
    expect(screen.getByText('Nhập & trả kết quả')).toBeInTheDocument();

    // Không hiển thị các chức năng trái quyền (Bác sĩ kê đơn / Quản trị viên):
    expect(screen.queryByText('Kê đơn thuốc điện tử')).not.toBeInTheDocument();
    expect(screen.queryByText('Quản lý bác sĩ')).not.toBeInTheDocument();
  });

  it('render bang hang doi chi dinh CLS, ket qua cho duyet, quick actions va fallback khi user null', () => {
    useAuthStore.setState({
      user: null,
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    expect(screen.getByText('Danh sách & hàng đợi chỉ định CLS')).toBeInTheDocument();
    expect(screen.getByText('CLS-2026-0041')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText('Kết quả chờ duyệt & trả')).toBeInTheDocument();
    expect(screen.getByText('5 phiếu')).toBeInTheDocument();
    expect(screen.getAllByText('KTV. Cận lâm sàng').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByText('Xác nhận chỉ định CLS'));
    expect(screen.getByText(/Đang chọn lối vào:/)).toBeInTheDocument();
  });

  it('loc danh sach chi dinh theo tu khoa tim kiem, nhom CLS va hien thi empty state khi khong khop', () => {
    useAuthStore.setState({
      user: null,
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    const categorySelect = screen.getByLabelText('Lọc nhóm cận lâm sàng');
    fireEvent.change(categorySelect, { target: { value: 'FUNCTIONAL' } });
    expect(screen.getByText('Hoàng Đức Em')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();

    fireEvent.change(categorySelect, { target: { value: 'ALL' } });
    const searchInput = screen.getByLabelText('Tìm kiếm chỉ định CLS');
    fireEvent.change(searchInput, { target: { value: 'CLS-2026-0044' } });

    expect(screen.getByText('Phạm Thu Dung')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();

    fireEvent.change(searchInput, { target: { value: 'KHONG-TON-TAI-999' } });
    expect(
      screen.getByText('Không tìm thấy chỉ định cận lâm sàng phù hợp với bộ lọc.'),
    ).toBeInTheDocument();
  });

  it('bao ve route /diagnostic bang RequireAuth — chuyen huong ve /login?returnUrl=%2Fdiagnostic khi chua login', () => {
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/diagnostic']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.queryByText('Danh sách & hàng đợi chỉ định CLS')).not.toBeInTheDocument();
    expect(screen.getByTestId('location-probe')).toHaveTextContent(
      '/login?returnUrl=%2Fdiagnostic',
    );
  });
});
