import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { PrescriptionSearchPage } from '../pages/prescription-search-page';

const SAMPLE_SEARCH_RESPONSE = {
  items: [
    {
      id: 88,
      prescriptionCode: 'RX-2026-0415-0088',
      visitCode: 'VIS-2026-0088',
      patientCode: 'PAT-2024-0108',
      patientName: 'NGUYỄN VĂN AN',
      gender: 'MALE',
      dateOfBirth: '1988-08-15',
      doctorName: 'TS.BS Nguyễn Minh Đức',
      status: 'ISSUED',
      issuedAt: '2026-11-24T08:30:00Z',
      itemCount: 4,
    },
    {
      id: 81,
      prescriptionCode: 'RX-2026-0415-0081',
      visitCode: 'VIS-2026-0081',
      patientCode: 'PAT-2024-0210',
      patientName: 'Vũ Văn Hùng',
      gender: 'MALE',
      dateOfBirth: '1975-03-10',
      doctorName: 'BS.CKI Vũ Đình Cương',
      status: 'PREPARING',
      issuedAt: '2026-11-24T08:15:00Z',
      itemCount: 2,
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 50,
};

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

describe('PrescriptionSearchPage (real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: SAMPLE_SEARCH_RESPONSE,
    } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetches GET /pharmacy/prescriptions and renders real metric counts & compliance banner', async () => {
    renderSearchPage();

    expect(await screen.findByText('RX-2026-0415-0088')).toBeInTheDocument();
    expect(screen.getByText('CHỜ TIẾP NHẬN & SOẠN')).toBeInTheDocument();
    expect(screen.getByText('ĐANG CHUẨN BỊ CHIA THUỐC')).toBeInTheDocument();
    expect(screen.getByText(/Quy chuẩn Dược lâm sàng & An toàn Cấp phát GPP/i)).toBeInTheDocument();
  });

  it('filters prescription list by query, status tab, and doctor dropdown', async () => {
    renderSearchPage();

    expect(await screen.findByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('Vũ Văn Hùng')).toBeInTheDocument();

    const input = screen.getByPlaceholderText(/Tìm theo mã đơn thuốc/i);
    fireEvent.change(input, { target: { value: 'Vũ Văn Hùng' } });

    expect(screen.getByText('Vũ Văn Hùng')).toBeInTheDocument();
    expect(screen.queryByText('RX-2026-0415-0088')).not.toBeInTheDocument();

    fireEvent.change(input, { target: { value: '' } });
    fireEvent.click(screen.getByRole('tab', { name: /Đang chuẩn bị/i }));
    expect(screen.getByText('RX-2026-0415-0081')).toBeInTheDocument();
    expect(screen.queryByText('RX-2026-0415-0088')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: /Tất cả/i }));
    const doctorSelect = screen.getByLabelText(/Lọc theo bác sĩ kê đơn/i);
    fireEvent.change(doctorSelect, { target: { value: 'BS.CKI Vũ Đình Cương' } });
    expect(screen.getByText('RX-2026-0415-0081')).toBeInTheDocument();
    expect(screen.queryByText('RX-2026-0415-0088')).not.toBeInTheDocument();
  });

  it('renders error alert when API fails instead of falling back to mock prescriptions', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('Máy chủ nhà thuốc bận'));
    renderSearchPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('Máy chủ nhà thuốc bận');
  });
});
