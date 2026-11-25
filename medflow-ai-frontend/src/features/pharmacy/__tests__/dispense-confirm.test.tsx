import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { DispenseConfirmPage } from '../pages/dispense-confirm-page';

const SAMPLE_DISPENSE_CONTEXT = {
  prescriptionId: 88,
  rxCode: 'RX-2026-0415-0088',
  ticketNo: 'PHA-012',
  visitCode: 'VIS-2026-0501',
  issuedAt: '24/11/2026 08:30',
  doctorName: 'TS.BS Nguyễn Minh Đức',
  totalPaidFormatted: 'Chưa cập nhật',
  paymentMethodLabel: 'ĐÃ XÁC NHẬN',
  paymentStatus: 'PAID',
  regimenBadge: 'Phác đồ ngoại trú 14 ngày',
  receiver: {
    fullName: 'NGUYỄN VĂN AN',
    patientCode: 'PAT-2024-0108',
    identityCard: '001088******',
    phone: '0912345678',
    receiverName: 'NGUYỄN VĂN AN',
    relationship: 'Bản thân',
    isIdentityVerified: true,
  },
  medications: [
    {
      id: 'item-101',
      orderIndex: 1,
      prescriptionItemId: 101,
      medicineId: 11,
      drugName: 'Bismuth subsalicylate 300mg',
      qtyLabel: '28 Viên',
      prescribedQuantity: 28,
      dispensedQuantity: 28,
      unit: 'Viên',
      batchCode: 'LO-2405',
      batchId: 1001,
      expiryDate: '30/12/2028',
      dosageInstruction: 'Uống 2 viên/lần trước ăn',
      warningNote: 'Có thể làm sẫm màu lưỡi tạm thời',
    },
  ],
  checks: [
    { id: 'chk-1', label: 'Đã đối chiếu tên thuốc và hàm lượng', checked: false },
    { id: 'chk-2', label: 'Đã hướng dẫn liều dùng trực tiếp', checked: false },
  ],
  canDispense: true,
  disabledReason: null,
  dispenseStatus: 'PENDING',
};

function renderDispensePage(entry = '/pharmacy/prescriptions/RX-2026-0415-0088/dispense') {
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
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route
            path="/pharmacy/prescriptions/:prescriptionCode/dispense"
            element={<DispenseConfirmPage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DispenseConfirmPage (real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: SAMPLE_DISPENSE_CONTEXT,
    } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('validates identity & pharmacist checks and calls POST /pharmacy/prescriptions/:code/dispense showing real dispenseCode', async () => {
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        dispenseCode: 'DISP-20261124-0901',
        rxCode: 'RX-2026-0415-0088',
        visitCode: 'VIS-2026-0501',
        ticketNo: 'PHA-012',
        patientName: 'NGUYỄN VĂN AN',
        patientCode: 'PAT-2024-0108',
        receiverName: 'NGUYỄN VĂN AN',
        receiverIdentityMasked: '001088******',
        prescribingDoctor: 'TS.BS Nguyễn Minh Đức',
        dispensingPharmacist: 'DS. Trần Thu Hương',
        dispensedAt: '24/11/2026 09:10',
        statusLabel: 'DISPENSED',
        paymentSummary: 'Chưa cập nhật',
        totalAmountFormatted: 'Chưa cập nhật',
        items: [],
        auditNote: 'Audit OK',
      },
    } as AxiosResponse);

    renderDispensePage();

    expect((await screen.findAllByText('NGUYỄN VĂN AN')).length).toBeGreaterThan(0);
    expect(await screen.findByText('Đã đối chiếu tên thuốc và hàm lượng')).toBeInTheDocument();

    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận Hoàn tất Bàn giao & Trừ tồn kho/i }),
    );
    expect(
      await screen.findByText(/Còn 2 mục kiểm tra bắt buộc của Dược sĩ chưa được xác nhận\./i),
    ).toBeInTheDocument();

    for (const cb of screen.getAllByRole('checkbox')) {
      if (!(cb as HTMLInputElement).checked) {
        fireEvent.click(cb);
      }
    }

    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận Hoàn tất Bàn giao & Trừ tồn kho/i }),
    );

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/pharmacy/prescriptions/RX-2026-0415-0088/dispense',
        expect.objectContaining({
          prescriptionIdOrCode: 'RX-2026-0415-0088',
          isIdentityVerified: true,
          checksConfirmed: true,
        }),
      );
      expect(screen.getAllByText(/DISP-20261124-0901/i).length).toBeGreaterThan(0);
      expect(screen.queryByText(/DISP-2026-0415-0042/i)).not.toBeInTheDocument();
    });
  });
});
