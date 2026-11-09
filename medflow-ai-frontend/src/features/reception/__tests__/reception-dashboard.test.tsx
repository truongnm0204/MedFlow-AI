import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';

import { ReceptionDashboardPage } from '../pages/reception-dashboard-page';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <ReceptionDashboardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ReceptionDashboardPage', () => {
  it('render dashboard header + stat cards', () => {
    useAuthStore.setState({
      user: {
        id: 5,
        email: 'letan@medflow.test',
        fullName: 'Lê Tân',
        roles: ['RECEPTION'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    // Label "Tổng quan quầy tiếp đón" xuất hiện ở cả sidebar nav và page header.
    expect(screen.getAllByText('Tổng quan quầy tiếp đón').length).toBeGreaterThan(0);
    expect(screen.getByText('Chờ check-in')).toBeInTheDocument();
    expect(screen.getByText('Đang trong hàng đợi')).toBeInTheDocument();
    // "Đang khám" xuất hiện ở cả stat card và queue status badge.
    expect(screen.getAllByText('Đang khám').length).toBeGreaterThan(0);
    expect(screen.getByText('Hoàn tất hôm nay')).toBeInTheDocument();
  });

  it('render queue table voi mock entries', () => {
    useAuthStore.setState({
      user: null,
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    expect(screen.getByText('Hàng đợi hiện tại')).toBeInTheDocument();
    expect(screen.getByText('A012')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
  });

  it('mo modal chuyen phong tuong thich tu bang hang doi va khoa ca dang kham', async () => {
    useAuthStore.setState({
      user: {
        id: 5,
        email: 'letan@medflow.test',
        fullName: 'Lê Tân',
        roles: ['RECEPTION'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    const user = userEvent.setup();
    renderPage();

    const inProgressBtn = screen.getByRole('button', {
      name: 'Chuyển phòng khám cho Nguyễn Văn An',
    });
    expect(inProgressBtn).toBeDisabled();

    const calledBtn = screen.getByRole('button', {
      name: 'Chuyển phòng khám cho Trần Thị Bích',
    });
    expect(calledBtn).not.toBeDisabled();

    await user.click(calledBtn);

    expect(
      await screen.findByText('Chuyển bệnh nhân sang phòng khám tương thích (UC27)'),
    ).toBeInTheDocument();
  });

  it('mo modal phan buong kham tu hang doi tiep don', async () => {
    useAuthStore.setState({
      user: {
        id: 5,
        email: 'reception01@medflow.test',
        fullName: 'Lễ tân Lê Lan',
        roles: ['RECEPTION'],
        patientCode: null,
      },
      accessToken: 'tok-rec',
      refreshToken: 'rtok-rec',
      isAuthenticated: true,
    });

    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: 'Phân buồng khám cho Lê Minh Cường' }));

    expect(await screen.findByText('Phân buồng khám lâm sàng (UC27)')).toBeInTheDocument();
    expect(screen.getByDisplayValue('VIS-202610-0009')).toBeInTheDocument();
  });

  it('an thao tac chuyen buong kham khi nguoi dung khong co quyen VISIT_ASSIGN', () => {
    useAuthStore.setState({
      user: {
        id: 99,
        email: 'staff@medflow.test',
        fullName: 'Nhân viên không có quyền điều phối',
        roles: ['PHARMACIST'],
        patientCode: null,
      },
      accessToken: 'tok-pharmacist',
      refreshToken: 'rtok-pharmacist',
      isAuthenticated: true,
    });

    renderPage();

    expect(
      screen.queryByRole('button', { name: 'Chuyển phòng khám cho Trần Thị Bích' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Chuyển phòng khám tương thích/i }),
    ).not.toBeInTheDocument();
  });

  it('bao ve route /reception bang RequireAuth va RequireReception', () => {
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc1 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = render(
      <QueryClientProvider client={qc1}>
        <MemoryRouter initialEntries={['/reception']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/login?returnUrl=%2Freception');
    unmount();

    useAuthStore.setState({
      user: {
        id: 1,
        email: 'patient@medflow.test',
        fullName: 'Bệnh nhân A',
        roles: ['PATIENT'],
        patientCode: 'PAT-001',
      },
      accessToken: 'tok-pat',
      refreshToken: 'rtok-pat',
      isAuthenticated: true,
    });

    const qc2 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc2}>
        <MemoryRouter initialEntries={['/reception']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/');
  });
});
