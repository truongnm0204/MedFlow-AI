import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { PrepareMedicinesPage } from '../pages/prepare-medicines-page';

const SAMPLE_PREPARE_CONTEXT = {
  prescriptionId: 88,
  rxCode: 'RX-2026-0415-0088',
  visitId: 501,
  visitCode: 'VIS-2026-0501',
  patientCode: 'PAT-2024-0108',
  patientName: 'NGUYỄN VĂN AN',
  gender: 'Nam',
  ageLabel: '38 tuổi',
  doctorName: 'TS.BS Nguyễn Minh Đức',
  roomLabel: 'P.201 Nội Tiêu Hóa',
  primaryDiagnosis: 'Viêm xung huyết hang vị',
  secondaryDiagnosis: 'Rối loạn tiêu hóa',
  criticalAllergy: 'DỊ ỨNG THUỐC ĐẶC BIỆT NGUY HIỂM: PENICILLIN',
  paymentBadge: 'ĐÃ THANH TOÁN VIỆN PHÍ',
  status: 'ISSUED',
  dispenseId: null,
  issuedAt: '2026-11-24T08:30:00Z',
  items: [
    {
      id: 101,
      orderIndex: 1,
      medicineId: 11,
      medicineCode: 'MED-011',
      drugName: 'Bismuth subsalicylate 300mg',
      activeIngredient: 'Bismuth subsalicylate',
      strength: '300mg',
      dosageForm: 'Viên nén nhai',
      usageInstruction: 'Uống 2 viên/lần x 2 lần/ngày trước ăn 30 phút',
      prescribedQty: 28,
      pickedQty: 28,
      unit: 'Viên',
      selectedBatchId: 1001,
      selectedBatchCode: 'LO-2405',
      isPrepared: false,
      availableBatches: [
        {
          batchId: 1001,
          batchCode: 'LO-2405',
          expiryDate: '30/12/2027',
          availableStock: 120,
          shelfLocation: 'Kệ A-01',
          isFefoRecommended: true,
        },
      ],
    },
  ],
  gppChecks: [
    {
      id: 'gpp-1',
      title: 'Đối chiếu 5 đúng',
      description: 'Đúng người bệnh, đúng thuốc, đúng liều, đúng đường dùng, đúng thời gian',
      checked: false,
    },
  ],
};

function renderPreparePage(entry = '/pharmacy/prescriptions/RX-2026-0415-0088/prepare') {
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
            path="/pharmacy/prescriptions/:prescriptionCode/prepare"
            element={<PrepareMedicinesPage />}
          />
          <Route path="/pharmacy/prepare" element={<PrepareMedicinesPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PrepareMedicinesPage (real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: SAMPLE_PREPARE_CONTEXT,
    } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders empty prompt when no prescriptionCode is supplied instead of mock patient', () => {
    renderPreparePage('/pharmacy/prepare');
    expect(
      screen.getByText(/Chưa chọn đơn thuốc cần chuẩn bị & kiểm tra lô FEFO/i),
    ).toBeInTheDocument();
    expect(screen.queryByText('NGUYỄN VĂN AN')).not.toBeInTheDocument();
  });

  it('fetches GET /pharmacy/prescriptions/:code/prepare and validates before POST', async () => {
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        prescriptionId: 88,
        prescriptionCode: 'RX-2026-0415-0088',
        dispenseId: 301,
        status: 'PREPARED',
        preparedAt: '2026-11-24T09:00:00Z',
        preparedByStaffName: 'DS. Trần Thu Hương',
        preparedItemCount: 1,
        message: 'Đã hoàn tất soạn đơn RX-2026-0415-0088 trên máy chủ.',
      },
    } as AxiosResponse);

    renderPreparePage();

    expect(await screen.findByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('Bismuth subsalicylate 300mg')).toBeInTheDocument();

    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận Đã soạn xong & Chuyển Phát thuốc/i }),
    );
    expect(
      screen.getByText(/Còn 1 khoản thuốc chưa được đánh dấu "Đã soạn đủ"\./i),
    ).toBeInTheDocument();

    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận đã soạn Bismuth subsalicylate 300mg/i }),
    );
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận Đã soạn xong & Chuyển Phát thuốc/i }),
    );

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/pharmacy/prescriptions/RX-2026-0415-0088/prepare',
        expect.objectContaining({
          items: [
            expect.objectContaining({
              prescriptionItemId: 101,
              medicineBatchId: 1001,
              pickedQuantity: 28,
            }),
          ],
        }),
      );
      expect(
        screen.getByText(/Đã hoàn tất soạn đơn RX-2026-0415-0088 trên máy chủ\./i),
      ).toBeInTheDocument();
    });
  });

  it('resets local prepared flag when background refetch reports insufficient batch stock', async () => {
    let batchStock = 120;
    vi.spyOn(apiClient, 'get').mockImplementation(
      async () =>
        ({
          data: {
            ...SAMPLE_PREPARE_CONTEXT,
            items: [
              {
                ...SAMPLE_PREPARE_CONTEXT.items[0],
                availableBatches: [
                  {
                    ...SAMPLE_PREPARE_CONTEXT.items[0].availableBatches[0],
                    availableStock: batchStock,
                  },
                ],
              },
            ],
          },
        }) as AxiosResponse,
    );

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
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/pharmacy/prescriptions/RX-2026-0415-0088/prepare']}>
          <Routes>
            <Route
              path="/pharmacy/prescriptions/:prescriptionCode/prepare"
              element={<PrepareMedicinesPage />}
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận đã soạn Bismuth subsalicylate 300mg/i }),
    );
    expect(screen.getByText('1/1 khoản')).toBeInTheDocument();

    // Background refetch drops batch stock below pickedQty (5 < 28) -> forces isPrepared back to false
    batchStock = 5;
    await qc.invalidateQueries({
      queryKey: ['pharmacy', 'prepare-context', 'RX-2026-0415-0088'],
    });

    await waitFor(() => {
      expect(screen.getByText('0/1 khoản')).toBeInTheDocument();
    });
  });

  it('resets local prepared flag when pinned default batch is locked/expired on refetch (both fallback batch shift and empty batches) and locks DISPENSED status', async () => {
    let availableBatches = [
      {
        batchId: 1001,
        batchCode: 'LO-2405',
        expiryDate: '30/12/2027',
        availableStock: 120,
        shelfLocation: 'Kệ A-01',
        isFefoRecommended: true,
      },
      {
        batchId: 1002,
        batchCode: 'LO-2412',
        expiryDate: '30/06/2028',
        availableStock: 90,
        shelfLocation: 'Kệ A-02',
        isFefoRecommended: false,
      },
    ];
    let currentSelectedCode = 'LO-2405';
    let currentStatus = 'ISSUED';

    vi.spyOn(apiClient, 'get').mockImplementation(
      async (url) =>
        ({
          data: {
            ...SAMPLE_PREPARE_CONTEXT,
            rxCode: String(url).includes('RX-SECOND-0099')
              ? 'RX-SECOND-0099'
              : SAMPLE_PREPARE_CONTEXT.rxCode,
            status: currentStatus,
            items: [
              {
                ...SAMPLE_PREPARE_CONTEXT.items[0],
                selectedBatchId: availableBatches[0]?.batchId ?? null,
                selectedBatchCode: currentSelectedCode,
                availableBatches,
              },
            ],
          },
        }) as AxiosResponse,
    );

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
    const { unmount } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/pharmacy/prescriptions/RX-2026-0415-0088/prepare']}>
          <Routes>
            <Route
              path="/pharmacy/prescriptions/:prescriptionCode/prepare"
              element={<PrepareMedicinesPage />}
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByText('NGUYỄN VĂN AN')).toBeInTheDocument();

    // Pharmacist marks default batch LO-2405 as prepared WITHOUT touching batch <select>
    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận đã soạn Bismuth subsalicylate 300mg/i }),
    );
    expect(screen.getByText('1/1 khoản')).toBeInTheDocument();

    // Case 1: LO-2405 expires/locks on server -> refetch returns only LO-2412; must NOT silently keep 1/1 prepared on LO-2412
    availableBatches = [availableBatches[1]];
    currentSelectedCode = 'LO-2412';
    await qc.invalidateQueries({
      queryKey: ['pharmacy', 'prepare-context', 'RX-2026-0415-0088'],
    });
    await waitFor(() => {
      expect(screen.getByText('0/1 khoản')).toBeInTheDocument();
    });

    // Mark LO-2412 prepared, then Case 2: all batches expire/lock -> availableBatches: []
    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận đã soạn Bismuth subsalicylate 300mg/i }),
    );
    expect(screen.getByText('1/1 khoản')).toBeInTheDocument();

    availableBatches = [];
    currentSelectedCode = '';
    await qc.invalidateQueries({
      queryKey: ['pharmacy', 'prepare-context', 'RX-2026-0415-0088'],
    });
    await waitFor(() => {
      expect(screen.getByText('0/1 khoản')).toBeInTheDocument();
    });

    // Case 3: Server DISPENSED disables primary confirm button
    currentStatus = 'DISPENSED';
    await qc.invalidateQueries({
      queryKey: ['pharmacy', 'prepare-context', 'RX-2026-0415-0088'],
    });
    await waitFor(() => {
      expect(
        screen.getByRole('button', { name: /Xác nhận Đã soạn xong & Chuyển Phát thuốc/i }),
      ).toBeDisabled();
    });
    unmount();

    // Case 4: Second prescription starts isolated
    currentStatus = 'ISSUED';
    renderPreparePage('/pharmacy/prescriptions/RX-SECOND-0099/prepare');
    expect(await screen.findByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('0/1 khoản')).toBeInTheDocument();
  });
});
