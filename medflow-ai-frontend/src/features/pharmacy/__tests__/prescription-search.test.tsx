import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { PrescriptionSearchPage } from '../pages/prescription-search-page';

function renderSearchPage() {
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
        <PrescriptionSearchPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PrescriptionSearchPage (Issue #141)', () => {
  it('renders 4 real-time pharmacy metric cards and compliance banner', () => {
    renderSearchPage();

    expect(screen.getByText('CHỜ TIẾP NHẬN & SOẠN')).toBeInTheDocument();
    expect(screen.getAllByText('06').length).toBeGreaterThan(0);
    expect(screen.getByText('ĐANG CHUẨN BỊ CHIA THUỐC')).toBeInTheDocument();
    expect(screen.getAllByText('03').length).toBeGreaterThan(0);
    expect(screen.getByText('ĐÃ SOẠN XONG - CHỜ GỌI')).toBeInTheDocument();
    expect(screen.getAllByText('04').length).toBeGreaterThan(0);
    expect(screen.getByText('ĐÃ HOÀN TẤT TRONG CA')).toBeInTheDocument();
    expect(screen.getByText('68')).toBeInTheDocument();
    expect(screen.getByText(/Quy chuẩn Dược lâm sàng & An toàn Cấp phát GPP/i)).toBeInTheDocument();
  });

  it('renders prescription list with allergy warning and searches by query', () => {
    renderSearchPage();

    expect(screen.getByText('RX-2026-0415-0088')).toBeInTheDocument();
    expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText(/DỊ ỨNG: Penicillin \(Phù mạch\)/i)).toBeInTheDocument();
    expect(screen.getByText('Vũ Văn Hùng')).toBeInTheDocument();

    const input = screen.getByPlaceholderText(/Tìm theo mã đơn thuốc/i);
    fireEvent.change(input, { target: { value: 'Vũ Văn Hùng' } });

    expect(screen.getByText('Vũ Văn Hùng')).toBeInTheDocument();
    expect(screen.queryByText('RX-2026-0415-0088')).not.toBeInTheDocument();
  });

  it('filters prescriptions by status tab and doctor dropdown', () => {
    renderSearchPage();

    fireEvent.click(screen.getByRole('tab', { name: /Đang chuẩn bị/i }));
    expect(screen.getByText('RX-2026-0415-0081')).toBeInTheDocument();
    expect(screen.queryByText('RX-2026-0415-0088')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: /Tất cả/i }));
    const doctorSelect = screen.getByLabelText(/Lọc theo bác sĩ kê đơn/i);
    fireEvent.change(doctorSelect, { target: { value: 'BS.CKI Vũ Đình Cương' } });
    expect(screen.getByText('RX-2026-0415-0079')).toBeInTheDocument();
    expect(screen.queryByText('RX-2026-0415-0088')).not.toBeInTheDocument();
  });

  it('shows notice when clicking Tiếp nhận & Soạn thuốc or Quét mã vạch', () => {
    renderSearchPage();

    fireEvent.click(screen.getAllByRole('button', { name: /Tiếp nhận & Soạn thuốc/i })[0]);
    expect(
      screen.getByText(/Đã tiếp nhận đơn thuốc RX-2026-0415-0088 của bệnh nhân NGUYỄN VĂN AN\./i),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Quét mã vạch/i }));
    expect(screen.getByText(/Đang kích hoạt đầu đọc mã vạch \[F2\]/i)).toBeInTheDocument();
  });
});
