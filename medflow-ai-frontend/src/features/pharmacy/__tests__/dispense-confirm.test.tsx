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

  it('enforces server DISPENSED status over local edits and disables confirmation button', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        ...SAMPLE_DISPENSE_CONTEXT,
        dispenseStatus: 'DISPENSED',
        canDispense: false,
      },
    } as AxiosResponse);

    renderDispensePage();

    expect((await screen.findAllByText('NGUYỄN VĂN AN')).length).toBeGreaterThan(0);
    expect(screen.getByText('ĐÃ BÀN GIAO & ĐÓNG CA')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Đã hoàn tất bàn giao & Đóng lượt \(DISPENSED\)/i }),
    ).toBeDisabled();
  });

  it('blocks confirmation when dispenseStatus is CANCELLED even if canDispense is omitted and isolates rxCode state', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        ...SAMPLE_DISPENSE_CONTEXT,
        canDispense: undefined,
        dispenseStatus: 'CANCELLED',
      },
    } as AxiosResponse);

    const { unmount } = renderDispensePage();
    expect((await screen.findAllByText('NGUYỄN VĂN AN')).length).toBeGreaterThan(0);
    expect(
      screen.getByRole('button', { name: /Xác nhận Hoàn tất Bàn giao & Trừ tồn kho/i }),
    ).toBeDisabled();
    unmount();

    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        ...SAMPLE_DISPENSE_CONTEXT,
        rxCode: 'RX-OTHER-0099',
        dispenseStatus: 'PENDING',
        canDispense: true,
      },
    } as AxiosResponse);
    renderDispensePage('/pharmacy/prescriptions/RX-OTHER-0099/dispense');
    expect((await screen.findAllByText('NGUYỄN VĂN AN')).length).toBeGreaterThan(0);
    expect(
      screen.getByRole('button', { name: /Xác nhận Hoàn tất Bàn giao & Trừ tồn kho/i }),
    ).not.toBeDisabled();
  });

  it('preserves scoped idempotencyKey across network retry and rotates after 409 conflict', async () => {
    const networkErr = new Error('Network timeout');
    const conflictErr = Object.assign(new Error('Conflict'), {
      isAxiosError: true,
      response: { status: 409, data: { detail: 'Số lượng tồn không đủ.' } },
    });

    const postSpy = vi
      .spyOn(apiClient, 'post')
      .mockRejectedValueOnce(networkErr)
      .mockRejectedValueOnce(conflictErr)
      .mockResolvedValueOnce({
        data: {
          dispenseCode: 'DISP-2026-1125-0088',
          rxCode: 'RX-2026-0415-0088',
          visitCode: 'VIS-2026-0501',
          ticketNo: 'PHA-012',
          patientName: 'NGUYỄN VĂN AN',
          patientCode: 'PAT-2024-0108',
          receiverName: 'NGUYỄN VĂN AN',
          receiverIdentityMasked: '001088******',
          prescribingDoctor: 'TS.BS Nguyễn Minh Đức',
          dispensingPharmacist: 'DS. Trần Thu Hương',
          dispensedAt: '25/11/2026 09:10',
          statusLabel: 'DISPENSED',
          paymentSummary: 'Chưa có dữ liệu',
          totalAmountFormatted: 'Chưa có dữ liệu',
          items: [],
          auditNote: 'Audit OK',
        },
      } as AxiosResponse);

    renderDispensePage();
    expect((await screen.findAllByText('NGUYỄN VĂN AN')).length).toBeGreaterThan(0);

    for (const cb of screen.getAllByRole('checkbox')) {
      if (!(cb as HTMLInputElement).checked) {
        fireEvent.click(cb);
      }
    }

    const submitBtn = screen.getByRole('button', {
      name: /Xác nhận Hoàn tất Bàn giao & Trừ tồn kho/i,
    });

    // Attempt 1 fails with transient network error
    fireEvent.click(submitBtn);
    expect(await screen.findByText('Network timeout')).toBeInTheDocument();
    expect(postSpy).toHaveBeenCalledTimes(1);
    const key1 = (postSpy.mock.calls[0]?.[1] as { idempotencyKey: string }).idempotencyKey;
    expect(key1.startsWith('88:')).toBe(true);

    // Attempt 2 retries same submit -> MUST reuse identical idempotencyKey, then fails with 409
    fireEvent.click(submitBtn);
    expect(await screen.findByText(/Số lượng tồn không đủ/i)).toBeInTheDocument();
    expect(postSpy).toHaveBeenCalledTimes(2);
    const key2 = (postSpy.mock.calls[1]?.[1] as { idempotencyKey: string }).idempotencyKey;
    expect(key2).toBe(key1);

    // Attempt 3 after 409 conflict -> MUST rotate to a fresh idempotencyKey
    fireEvent.click(submitBtn);
    await waitFor(() => expect(postSpy).toHaveBeenCalledTimes(3));
    const key3 = (postSpy.mock.calls[2]?.[1] as { idempotencyKey: string }).idempotencyKey;
    expect(key3.startsWith('88:')).toBe(true);
    expect(key3).not.toBe(key1);
  });

  it('blocks under-quantity dispense (issue #864): shows khong du thuoc, bao benh nhan and never calls API', async () => {
    const postSpy = vi.spyOn(apiClient, 'post');

    renderDispensePage();
    expect((await screen.findAllByText('NGUYỄN VĂN AN')).length).toBeGreaterThan(0);

    for (const cb of screen.getAllByRole('checkbox')) {
      if (!(cb as HTMLInputElement).checked) {
        fireEvent.click(cb);
      }
    }

    // Cài đặt số lượng thực xuất thấp hơn kê đơn (kể cả 0) — cấm cấp phát thiếu.
    const qtyInput = screen.getByLabelText(/Số lượng thực xuất \(Viên\)/i);
    fireEvent.change(qtyInput, { target: { value: '0' } });

    const submitBtn = screen.getByRole('button', {
      name: /Xác nhận Hoàn tất Bàn giao & Trừ tồn kho/i,
    });
    fireEvent.click(submitBtn);

    // Hiển thị rõ không đủ thuốc + báo bệnh nhân; không gọi API, không có ô lý do thiếu.
    expect(await screen.findByText(/Không đủ thuốc \(thiếu so với kê đơn\)/i)).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();
    expect(screen.queryByLabelText(/Lý do cấp thiếu/i)).not.toBeInTheDocument();
  });

  it('surfaces patient-facing guidance when backend rejects with 409 insufficient stock (issue #864)', async () => {
    const stockErr = Object.assign(new Error('Không đủ tồn kho để cấp phát đầy đủ'), {
      isAxiosError: true,
      response: { status: 409, data: { detail: 'Không đủ tồn kho để cấp phát đầy đủ' } },
    });
    const postSpy = vi.spyOn(apiClient, 'post').mockRejectedValueOnce(stockErr);

    renderDispensePage();
    expect((await screen.findAllByText('NGUYỄN VĂN AN')).length).toBeGreaterThan(0);

    for (const cb of screen.getAllByRole('checkbox')) {
      if (!(cb as HTMLInputElement).checked) {
        fireEvent.click(cb);
      }
    }

    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận Hoàn tất Bàn giao & Trừ tồn kho/i }),
    );

    // Lỗi backend + guidance báo bệnh nhân/kết thúc luồng phải hiển thị cùng nhau.
    await waitFor(() => expect(postSpy).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(/báo bệnh nhân và kết thúc luồng/i)).toBeInTheDocument();
    expect(screen.getByText(/Không đủ tồn kho để cấp phát đầy đủ/i)).toBeInTheDocument();
  });
});
