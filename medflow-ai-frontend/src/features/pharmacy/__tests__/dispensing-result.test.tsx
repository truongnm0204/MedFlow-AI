import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { DispensingResultPage } from '../pages/dispensing-result-page';

function renderResultPage() {
  useAuthStore.setState({
    user: {
      id: 6,
      email: 'duocsi@medflow.test',
      fullName: 'DS. Đỗ Thị Lan Anh',
      roles: ['PHARMACY_STAFF'],
      patientCode: null,
    },
    accessToken: 'tok',
    refreshToken: 'rtok',
    isAuthenticated: true,
  });

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <DispensingResultPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DispensingResultPage (Issue #149)', () => {
  it('renders handover confirmation banner, receipt code and patient info', () => {
    renderResultPage();

    expect(screen.getByText(/XÁC NHẬN BÀN GIAO THUỐC NGOẠI TRÚ THÀNH CÔNG/i)).toBeInTheDocument();
    expect(screen.getAllByText('DISP-2026-0415-0042').length).toBeGreaterThan(0);
    expect(screen.getByText('DISPENSED & CLOSED')).toBeInTheDocument();
    expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('1.293.600 đ')).toBeInTheDocument();
  });

  it('renders all 4 dispensed items with batch codes and triggers PDF / print actions', () => {
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    renderResultPage();

    expect(screen.getByText('Bismuth subsalicylate 300mg')).toBeInTheDocument();
    expect(screen.getByText('LO-2405')).toBeInTheDocument();
    expect(screen.getByText('KL-2410')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Tải biên nhận PDF/i }));
    expect(screen.getByText(/Đã xuất biên nhận PDF DISP-2026-0415-0042\.pdf/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /In biên nhận bàn giao/i }));
    expect(printSpy).toHaveBeenCalled();

    printSpy.mockRestore();
  });
});
