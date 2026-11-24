import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { DispensingResultPage } from '../pages/dispensing-result-page';

const SAMPLE_RECEIPT = {
  dispenseCode: 'DISP-20261124-0901',
  rxCode: 'RX-2026-0415-0088',
  visitCode: 'VIS-2026-0501',
  ticketNo: 'PHA-012',
  patientName: 'NGUYỄN VĂN AN',
  patientCode: 'PAT-2024-0108',
  receiverName: 'NGUYỄN VĂN AN',
  receiverIdentityMasked: '001088******',
  prescribingDoctor: 'TS.BS Nguyễn Minh Đức',
  dispensingPharmacist: 'DS. Đỗ Thị Lan Anh',
  dispensedAt: '24/11/2026 09:15',
  statusLabel: 'DISPENSED & CLOSED',
  paymentSummary: 'Chưa cập nhật',
  totalAmountFormatted: 'Chưa cập nhật',
  items: [
    {
      id: 'line-1',
      orderIndex: 1,
      drugName: 'Bismuth subsalicylate 300mg',
      dosageForm: 'Viên nén',
      qtyDispensed: 28,
      unit: 'Viên',
      batchCode: 'LO-2405',
      expiryDate: '30/12/2028',
      shelfLocation: 'Chưa có dữ liệu',
      unitPriceFormatted: 'Chưa cập nhật',
      lineTotalFormatted: 'Chưa cập nhật',
    },
  ],
  auditNote: 'Đã ghi nhận biên nhận cấp phát thuốc.',
};

function renderResultPage(entry = '/pharmacy/dispensing/DISP-20261124-0901') {
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
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/pharmacy/dispensing/:dispenseCode" element={<DispensingResultPage />} />
          <Route path="/pharmacy/dispensing-result" element={<DispensingResultPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DispensingResultPage (real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: SAMPLE_RECEIPT,
    } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetches GET /pharmacy/dispenses/:code and renders unpriced ADR-0047 receipt cleanly', async () => {
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    renderResultPage();

    expect(
      await screen.findByText(/XÁC NHẬN BÀN GIAO THUỐC NGOẠI TRÚ THÀNH CÔNG/i),
    ).toBeInTheDocument();
    expect(screen.getAllByText('DISP-20261124-0901').length).toBeGreaterThan(0);
    expect(screen.getByText('Bismuth subsalicylate 300mg')).toBeInTheDocument();
    expect(screen.queryByText('1.293.600 đ')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /In biên nhận bàn giao/i }));
    expect(printSpy).toHaveBeenCalled();
  });

  it('surfaces error alert on 404 instead of falling back to DISP-2026-0415-0042 mock', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('Không tìm thấy phiếu cấp phát'));
    renderResultPage('/pharmacy/dispensing/DISP-MISSING');

    expect(await screen.findByRole('alert')).toHaveTextContent('Không tìm thấy phiếu cấp phát');
    expect(screen.queryByText('DISP-2026-0415-0042')).not.toBeInTheDocument();
  });
});
