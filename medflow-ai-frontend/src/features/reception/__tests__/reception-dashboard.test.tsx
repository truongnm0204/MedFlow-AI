import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { ReceptionDashboardPage } from '../pages/reception-dashboard-page';

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
});
