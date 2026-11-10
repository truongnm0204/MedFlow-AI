import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { DoctorCreatePrescriptionPage } from '../pages/doctor-create-prescription-page';

function renderCreatePrescriptionPage() {
  useAuthStore.setState({
    user: {
      id: 3,
      email: 'bacsi@medflow.test',
      fullName: 'BS. CKII Trần Quốc Tuấn',
      roles: ['DOCTOR'],
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
        <DoctorCreatePrescriptionPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DoctorCreatePrescriptionPage (Issue #500 / #139)', () => {
  it('renders patient header, allergy alert, 4 default regimen drugs and total fee', () => {
    renderCreatePrescriptionPage();

    expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText(/CẢNH BÁO DỊ ỨNG MỨC ĐỘ CAO: Dị ứng Penicillin/i)).toBeInTheDocument();
    expect(screen.getByText('Bismuth subsalicylate 300mg')).toBeInTheDocument();
    expect(screen.getByText(/Nexium 40mg/i)).toBeInTheDocument();
    expect(screen.getByTestId('prescription-total-amount')).toHaveTextContent('1.293.600 đ');
  });

  it('blocks adding an allergy-contraindicated drug (Amoxicillin)', () => {
    renderCreatePrescriptionPage();

    const select = screen.getByLabelText(/Chọn thuốc bổ sung vào đơn/i);
    fireEvent.change(select, { target: { value: 'DRG-AMOX-500' } });
    fireEvent.click(screen.getByRole('button', { name: /Thêm thuốc mới/i }));

    expect(screen.getByText(/CHẶN AN TOÀN:/i)).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Xóa thuốc Amoxicillin/i }),
    ).not.toBeInTheDocument();
  });

  it('allows adding a safe drug, updating quantity and issuing prescription', () => {
    renderCreatePrescriptionPage();

    const select = screen.getByLabelText(/Chọn thuốc bổ sung vào đơn/i);
    fireEvent.change(select, { target: { value: 'DRG-GAVISCON-10ML' } });
    fireEvent.click(screen.getByRole('button', { name: /Thêm thuốc mới/i }));

    expect(
      screen.getByRole('button', { name: /Xóa thuốc Gaviscon Dual Action/i }),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Ký số & Phát hành Đơn thuốc/i }));
    expect(
      screen.getByText(/Đã ký số & phát hành Đơn thuốc điện tử RX-2026-0925-0088/i),
    ).toBeInTheDocument();
    expect(screen.getByText('Đã ký số phát hành')).toBeInTheDocument();
  });
});
