import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
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
