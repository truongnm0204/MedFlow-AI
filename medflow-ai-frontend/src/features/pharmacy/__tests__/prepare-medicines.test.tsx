import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { PrepareMedicinesPage } from '../pages/prepare-medicines-page';

function renderPreparePage() {
  useAuthStore.setState({
    user: {
      id: 6,
      email: 'duocsi@medflow.test',
      fullName: 'DS. Trần Thu Hương',
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
        <PrepareMedicinesPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PrepareMedicinesPage (Issue #143)', () => {
  it('renders patient header, critical allergy alert and all 4 prescribed drugs', () => {
    renderPreparePage();

    expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('PAT-2024-0108')).toBeInTheDocument();
    expect(screen.getAllByText('RX-2026-0415-0088').length).toBeGreaterThan(0);
    expect(screen.getByText(/DỊ ỨNG THUỐC ĐẶC BIỆT NGUY HIỂM: PENICILLIN/i)).toBeInTheDocument();

    expect(screen.getByText('Bismuth subsalicylate 300mg')).toBeInTheDocument();
    expect(screen.getByText(/Nexium 40mg/i)).toBeInTheDocument();
    expect(screen.getByText(/Tetracycline Hydrochloride 500mg/i)).toBeInTheDocument();
    expect(screen.getByText(/Klion 500mg/i)).toBeInTheDocument();
  });

  it('shows error if confirming preparation before items are prepared or quantities mismatch', () => {
    renderPreparePage();

    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận Đã soạn xong & Chuyển Phát thuốc/i }),
    );
    expect(
      screen.getByText(/Còn 4 khoản thuốc chưa được đánh dấu "Đã soạn đủ"\./i),
    ).toBeInTheDocument();

    const qtyInput = screen.getByLabelText(/Số lượng thực soạn Bismuth subsalicylate 300mg/i);
    fireEvent.change(qtyInput, { target: { value: '10' } });
    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận Đã soạn xong & Chuyển Phát thuốc/i }),
    );
    expect(
      screen.getByText(/Số lượng thực soạn chưa khớp y lệnh tại 1 khoản thuốc/i),
    ).toBeInTheDocument();
  });

  it('completes preparation when all 4 medicines and 4 GPP checks are completed', () => {
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    renderPreparePage();

    // Mark all 4 items prepared
    const prepButtons = screen.getAllByRole('button', { name: /Xác nhận đã soạn/i });
    for (const btn of prepButtons) {
      fireEvent.click(btn);
    }

    // Check remaining 2 GPP checkboxes
    const checkboxes = screen.getAllByRole('checkbox');
    for (const cb of checkboxes) {
      if (!(cb as HTMLInputElement).checked) {
        fireEvent.click(cb);
      }
    }

    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận Đã soạn xong & Chuyển Phát thuốc/i }),
    );

    expect(screen.getByText(/Đã hoàn tất soạn đơn RX-2026-0415-0088/i)).toBeInTheDocument();
    expect(screen.getByText(/Đã soạn xong • Chờ phát/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /In nhãn hướng dẫn uống thuốc/i }));
    expect(printSpy).toHaveBeenCalled();

    printSpy.mockRestore();
  });
});
