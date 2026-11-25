import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { FefoBatchSelectionPage } from '../pages/fefo-batch-selection-page';

const SAMPLE_FEFO_CONTEXT = {
  prescriptionId: 88,
  prescriptionCode: 'RX-2026-0415-0088',
  visitId: 501,
  visitCode: 'VIS-2026-0501',
  patientCode: 'PAT-2024-0108',
  patientName: 'NGUYỄN VĂN AN',
  status: 'ISSUED',
  dispenseId: null,
  todayHospitalDate: '2026-11-25',
  items: [
    {
      prescriptionItemId: 101,
      orderIndex: 1,
      medicineId: 11,
      medicineCode: 'MED-011',
      drugName: 'Bismuth subsalicylate 300mg',
      activeIngredient: null,
      strength: '300mg',
      dosageForm: 'Viên nén',
      requiredQuantity: 28,
      unit: 'Viên',
      selectedBatchId: 1001,
      selectedBatchCode: 'LO-2405',
      overrideReason: null,
      batches: [
        {
          batchId: 1001,
          batchCode: 'LO-2405',
          expiryDate: '30/12/2026',
          expiryDateIso: '2026-12-30',
          daysUntilExpiry: 35,
          availableStock: 100,
          shelfLocation: 'Chưa cập nhật',
          status: 'FEFO_RECOMMENDED',
          isFefoRecommended: true,
          isSelectable: true,
          blockReason: null,
        },
        {
          batchId: 1002,
          batchCode: 'LO-2412',
          expiryDate: '30/06/2027',
          expiryDateIso: '2027-06-30',
          daysUntilExpiry: 217,
          availableStock: 80,
          shelfLocation: 'Chưa cập nhật',
          status: 'AVAILABLE',
          isFefoRecommended: false,
          isSelectable: true,
          blockReason: null,
        },
        {
          batchId: 1003,
          batchCode: 'LO-2001',
          expiryDate: '01/01/2020',
          expiryDateIso: '2020-01-01',
          daysUntilExpiry: -2520,
          availableStock: 15,
          shelfLocation: 'Chưa cập nhật',
          status: 'EXPIRED',
          isFefoRecommended: false,
          isSelectable: false,
          blockReason: 'Lô đã quá hạn sử dụng theo kiểm định GPP — Hệ thống khóa tự động',
        },
      ],
    },
  ],
};

function renderFefoPage(entry = '/pharmacy/prescriptions/RX-2026-0415-0088/fefo-batches') {
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
            path="/pharmacy/prescriptions/:prescriptionCode/fefo-batches"
            element={<FefoBatchSelectionPage />}
          />
          <Route path="/pharmacy/fefo-batches" element={<FefoBatchSelectionPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('FefoBatchSelectionPage (UC45 / GH-818 real /fefo-batches contract)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: SAMPLE_FEFO_CONTEXT,
    } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('submits FEFO_RECOMMENDED batch directly to POST /pharmacy/prescriptions/:code/fefo-batches', async () => {
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        prescriptionId: 88,
        prescriptionCode: 'RX-2026-0415-0088',
        dispenseId: 301,
        status: 'PREPARING',
        selectedAt: '2026-11-25T02:00:00Z',
        selectedByStaffName: 'DS. Trần Thu Hương',
        itemCount: 1,
        message: 'Đã xác nhận phân bổ lô thuốc theo nguyên tắc FEFO cho 1 khoản thuốc.',
      },
    } as AxiosResponse);

    renderFefoPage();

    expect(await screen.findByText('Bismuth subsalicylate 300mg')).toBeInTheDocument();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/pharmacy/prescriptions/RX-2026-0415-0088/fefo-batches',
    );
    expect(screen.getByRole('button', { name: /LO-2001/i })).toBeDisabled();

    fireEvent.click(screen.getByRole('button', { name: /Xác nhận chọn lô FEFO/i }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/pharmacy/prescriptions/RX-2026-0415-0088/fefo-batches',
        expect.objectContaining({
          items: [
            expect.objectContaining({
              prescriptionItemId: 101,
              medicineBatchId: 1001,
              batchCode: 'LO-2405',
              overrideReason: null,
            }),
          ],
        }),
      );
    });
  });

  it('requires valid override reason when selecting later-expiry AVAILABLE batch over FEFO_RECOMMENDED', async () => {
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        prescriptionId: 88,
        prescriptionCode: 'RX-2026-0415-0088',
        dispenseId: 301,
        status: 'PREPARING',
        selectedAt: '2026-11-25T02:05:00Z',
        selectedByStaffName: 'DS. Trần Thu Hương',
        itemCount: 1,
        message: 'Đã xác nhận phân bổ lô thuốc theo nguyên tắc FEFO cho 1 khoản thuốc.',
      },
    } as AxiosResponse);

    renderFefoPage();

    expect(await screen.findByText('Bismuth subsalicylate 300mg')).toBeInTheDocument();

    // Select later-expiry batch LO-2412
    fireEvent.click(screen.getByRole('button', { name: /LO-2412/i }));

    // Try submitting without reason -> blocked on client
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận chọn lô FEFO/i }));
    expect(await screen.findByText(/Vui lòng nhập lý do hợp lệ/i)).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();

    // Enter valid reason and submit
    const reasonInput = screen.getByLabelText(/Lý do chọn lô hạn dùng xa hơn lô ưu tiên FEFO/i);
    fireEvent.change(reasonInput, {
      target: { value: 'Lô hạn gần đang kiểm kê tại kho chính' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận chọn lô FEFO/i }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/pharmacy/prescriptions/RX-2026-0415-0088/fefo-batches',
        expect.objectContaining({
          items: [
            expect.objectContaining({
              prescriptionItemId: 101,
              medicineBatchId: 1002,
              batchCode: 'LO-2412',
              overrideReason: 'Lô hạn gần đang kiểm kê tại kho chính',
            }),
          ],
        }),
      );
      expect(
        screen.getByText(/Đã xác nhận phân bổ lô thuốc theo nguyên tắc FEFO/i),
      ).toBeInTheDocument();
    });
  });

  it('discards stale local batch override when server refetch reports depleted stock and isolates edits across prescriptions', async () => {
    let availableStockFor1002 = 80;
    vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
      if (String(url).includes('RX-OTHER-0099')) {
        return {
          data: {
            ...SAMPLE_FEFO_CONTEXT,
            prescriptionId: 99,
            prescriptionCode: 'RX-OTHER-0099',
            patientName: 'TRẦN THỊ BÌNH',
          },
        } as AxiosResponse;
      }
      return {
        data: {
          ...SAMPLE_FEFO_CONTEXT,
          items: [
            {
              ...SAMPLE_FEFO_CONTEXT.items[0],
              batches: SAMPLE_FEFO_CONTEXT.items[0].batches.map((b) =>
                b.batchId === 1002 ? { ...b, availableStock: availableStockFor1002 } : b,
              ),
            },
          ],
        },
      } as AxiosResponse;
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
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

    const { unmount } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/pharmacy/prescriptions/RX-2026-0415-0088/fefo-batches']}>
          <Routes>
            <Route
              path="/pharmacy/prescriptions/:prescriptionCode/fefo-batches"
              element={<FefoBatchSelectionPage />}
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByText('Bismuth subsalicylate 300mg')).toBeInTheDocument();

    // Pharmacist selects batch LO-2412 locally -> override reason input appears
    fireEvent.click(screen.getByRole('button', { name: /LO-2412/i }));
    expect(
      screen.getByLabelText(/Lý do chọn lô hạn dùng xa hơn lô ưu tiên FEFO/i),
    ).toBeInTheDocument();

    // Another pharmacist depletes LO-2412 below requiredQuantity (10 < 28), then background refetch occurs
    availableStockFor1002 = 10;
    await qc.invalidateQueries({
      queryKey: ['pharmacy', 'fefo-batches', 'RX-2026-0415-0088'],
    });

    // Stale override for LO-2412 is invalidated -> falls back to server FEFO_RECOMMENDED LO-2405 (no override reason box)
    await waitFor(() => {
      expect(
        screen.queryByLabelText(/Lý do chọn lô hạn dùng xa hơn lô ưu tiên FEFO/i),
      ).not.toBeInTheDocument();
    });
    unmount();

    // Opening a second prescription starts clean without leaking notices
    renderFefoPage('/pharmacy/prescriptions/RX-OTHER-0099/fefo-batches');
    expect(await screen.findByText('Bismuth subsalicylate 300mg')).toBeInTheDocument();
    expect(screen.queryByText(/Đã chọn lô LO-2412/i)).not.toBeInTheDocument();
  });
});
