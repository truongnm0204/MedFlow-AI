import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { DispenseConfirmPage } from '../pages/dispense-confirm-page';

function renderDispensePage() {
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
        <DispenseConfirmPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DispenseConfirmPage (Issue #147)', () => {
  it('renders receiver identity card, prescription metadata and 4 medications', () => {
    renderDispensePage();

    expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('001092018892')).toBeInTheDocument();
    expect(screen.getAllByText('RX-2026-0415-0088').length).toBeGreaterThan(0);
    expect(screen.getByText(/Bismuth subsalicylate 300mg/i)).toBeInTheDocument();
    expect(screen.getByText(/Klion 500mg/i)).toBeInTheDocument();
  });

  it('validates identity verification and pharmacist checks before dispensing', () => {
    renderDispensePage();

    // Try confirming before checking all 3 compulsory checks
    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận Hoàn tất Bàn giao & Trừ tồn kho/i }),
    );
    expect(
      screen.getByText(/Còn 2 mục kiểm tra bắt buộc của Dược sĩ chưa được xác nhận\./i),
    ).toBeInTheDocument();

    // Uncheck identity verification to test identity guard
    const idCheckbox = screen.getByRole('checkbox', {
      name: /ĐÃ ĐỐI CHIẾU TRÙNG KHỚP 100%/i,
    });
    fireEvent.click(idCheckbox);
    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận Hoàn tất Bàn giao & Trừ tồn kho/i }),
    );
    expect(
      screen.getByText(/Bắt buộc xác nhận đối chiếu định danh CCCD của người nhận thuốc/i),
    ).toBeInTheDocument();
  });

  it('completes dispensing and shows receipt notice when all checks pass', () => {
    renderDispensePage();

    const checkboxes = screen.getAllByRole('checkbox');
    for (const cb of checkboxes) {
      if (!(cb as HTMLInputElement).checked) {
        fireEvent.click(cb);
      }
    }

    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận Hoàn tất Bàn giao & Trừ tồn kho/i }),
    );

    expect(
      screen.getByText(/Đã hoàn tất bàn giao đơn thuốc RX-2026-0415-0088/i),
    ).toBeInTheDocument();
    expect(screen.getByText(/ĐÃ BÀN GIAO & ĐÓNG CA/i)).toBeInTheDocument();
  });
});
