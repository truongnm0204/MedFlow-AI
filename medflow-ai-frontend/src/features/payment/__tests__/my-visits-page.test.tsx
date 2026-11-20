import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { MyVisitsPage } from '../pages/my-visits-page';
import type { PatientVisitListItemDto } from '../types/payment.types';

vi.mock('@/shared/components/layout/app-header', () => ({
  AppHeader: () => <div data-testid="app-header" />,
}));
vi.mock('@/shared/components/layout/patient-footer', () => ({
  PatientFooter: () => <div data-testid="patient-footer" />,
}));

const baseVisit: PatientVisitListItemDto = {
  visitId: 10,
  visitCode: 'VIS-202611-0001',
  visitStatus: 'CHECKED_IN',
  checkedInAt: '2026-11-14T09:00:00',
  completedAt: null,
  hospitalName: 'Bệnh viện A',
  paymentStatus: null,
  paymentCode: null,
};

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/patient/visits']}>
        <Routes>
          <Route path="/patient/visits" element={<MyVisitsPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('MyVisitsPage', () => {
  it('lists visits with status, hospital and payment link', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: { items: [{ ...baseVisit, paymentStatus: 'PENDING', paymentCode: 'MF5' }] },
    });

    renderPage();

    expect(await screen.findByText('#VIS-202611-0001')).toBeInTheDocument();
    expect(screen.getByText('Bệnh viện A')).toBeInTheDocument();
    expect(screen.getByText('Chờ thanh toán')).toBeInTheDocument();
    const link = screen.getByRole('link', { name: /Thanh toán viện phí/i });
    expect(link).toHaveAttribute('href', '/payments/visits/VIS-202611-0001');
  });

  it('shows the paid label and receipt link', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: { items: [{ ...baseVisit, paymentStatus: 'PAID', paymentCode: 'MF5' }] },
    });

    renderPage();

    expect(await screen.findByText('Đã thanh toán')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Xem biên nhận/i })).toBeInTheDocument();
  });

  it('blocks closed visits', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: { items: [{ ...baseVisit, visitStatus: 'CANCELLED' }] },
    });

    renderPage();

    expect(await screen.findByText('Đã hủy')).toBeInTheDocument();
    expect(screen.getByText('Đã đóng')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Thanh toán viện phí/i })).not.toBeInTheDocument();
  });

  it('renders an empty state', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: { items: [] } });

    renderPage();

    expect(await screen.findByText('Chưa có lượt khám nào')).toBeInTheDocument();
  });

  it('renders the error state', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('Máy chủ không phản hồi'));

    renderPage();

    expect(await screen.findByText('Máy chủ không phản hồi')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Thử lại/i })).toBeInTheDocument();
  });
});
