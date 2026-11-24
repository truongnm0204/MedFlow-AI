import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { FefoBatchSelectionPage } from '../pages/fefo-batch-selection-page';

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
  roomLabel: 'P.201',
  primaryDiagnosis: 'Viêm dạ dày',
  secondaryDiagnosis: null,
  criticalAllergy: null,
  paymentBadge: 'PAID',
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
      activeIngredient: null,
      strength: '300mg',
      dosageForm: 'Viên nén',
      usageInstruction: '2 viên/lần',
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
          expiryDate: '30/12/2028',
          availableStock: 100,
          shelfLocation: 'Kệ A-01',
          isFefoRecommended: true,
        },
        {
          batchId: 1002,
          batchCode: 'LO-2412',
          expiryDate: '30/06/2029',
          availableStock: 80,
          shelfLocation: 'Kệ A-02',
          isFefoRecommended: false,
        },
        {
          batchId: 1003,
          batchCode: 'LO-2001',
          expiryDate: '01/01/2020',
          availableStock: 15,
          shelfLocation: 'Kho cách ly',
          isFefoRecommended: false,
        },
      ],
    },
  ],
  gppChecks: [],
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

describe('FefoBatchSelectionPage (real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: SAMPLE_PREPARE_CONTEXT,
    } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('disables expired batches and submits chosen FEFO batch to POST /pharmacy/prescriptions/:code/prepare', async () => {
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        prescriptionId: 88,
        prescriptionCode: 'RX-2026-0415-0088',
        dispenseId: 301,
        status: 'PREPARED',
        preparedAt: '2026-11-24T09:05:00Z',
        preparedByStaffName: 'DS. Trần Thu Hương',
        preparedItemCount: 1,
        message: 'OK',
      },
    } as AxiosResponse);

    renderFefoPage();

    expect(await screen.findByText('Bismuth subsalicylate 300mg')).toBeInTheDocument();
    const expiredBtn = screen.getByRole('button', { name: /LO-2001/i });
    expect(expiredBtn).toBeDisabled();

    fireEvent.click(screen.getByRole('button', { name: /LO-2412/i }));
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận chọn lô FEFO/i }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/pharmacy/prescriptions/RX-2026-0415-0088/prepare',
        expect.objectContaining({
          items: [
            expect.objectContaining({
              prescriptionItemId: 101,
              medicineBatchId: 1002,
              batchCode: 'LO-2412',
            }),
          ],
        }),
      );
      expect(
        screen.getByText(/Đã xác nhận phân bổ lô thuốc theo nguyên tắc FEFO/i),
      ).toBeInTheDocument();
    });
  });
});
