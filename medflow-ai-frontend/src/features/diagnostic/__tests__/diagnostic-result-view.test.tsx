import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { DiagnosticResultViewPage } from '../pages/diagnostic-result-view-page';

const SAMPLE_VIEW_DETAIL = {
  hospitalId: 1,
  hospitalCode: 'HOS-01',
  hospitalName: 'Bệnh viện Đa khoa MedFlow',
  generatedAtUtc: '2026-11-24T10:00:00Z',
  item: {
    orderId: 142,
    orderCode: 'ORD-2026-0415-0142',
    visitId: 501,
    visitCode: 'VIS-2026-0501',
    visitStatus: 'IN_PROGRESS',
    patient: {
      patientId: 108,
      patientCode: 'PAT-2024-0108',
      fullName: 'TRẦN MINH QUÂN',
      dateOfBirth: '1990-05-12',
      age: 36,
      gender: 'MALE',
      phone: '0909123456',
      bloodType: null,
      allergies: 'Dị ứng Penicillin',
    },
    orderingDoctorName: 'BS.CKI Nguyễn Minh Đức',
    serviceId: 30,
    serviceCode: 'SRV-ENDO-01',
    serviceName: 'Nội soi dạ dày tá tràng',
    category: 'IMAGING',
    unitPrice: 850000,
    priority: 'NORMAL',
    orderStatus: 'COMPLETED',
    departmentName: 'Khoa Thăm dò Chức năng',
    roomCode: 'R-202',
    roomName: 'Phòng Nội soi 202',
    servicePointName: 'Bàn Nội soi 01',
    instruction: 'Nhịn ăn 6 giờ',
    orderedAt: '2026-11-24T08:00:00Z',
    completedAt: '2026-11-24T09:15:00Z',
    resultId: 901,
    resultStatus: 'FINALIZED',
    isResultFinalized: true,
    resultFinalizedAt: '2026-11-24T09:30:00Z',
    finalizedByStaffName: 'BS.CKI Hoàng Minh Hải',
    latestVersionNo: 1,
    resultText: 'Thực quản thông thoáng. Niêm mạc hang vị xung huyết nhẹ.',
    conclusion: 'Viêm xung huyết hang vị mức độ nhẹ.',
    document: {
      reportFileUrl: 'https://files.medflow.test/diagnostic/ORD-2026-0415-0142.pdf',
      hasDocument: true,
      fileName: 'KQ-ORD-2026-0415-0142.pdf',
    },
    enteredByStaffName: 'BS.CKI Hoàng Minh Hải',
    enteredAt: '2026-11-24T09:25:00Z',
    verifiedByStaffName: 'BS.CKI Hoàng Minh Hải',
    verifiedAt: '2026-11-24T09:30:00Z',
    correctionReason: null,
  },
  clinicalContext: {
    visitId: 501,
    visitCode: 'VIS-2026-0501',
    visitStatus: 'IN_PROGRESS',
    reasonForVisit: 'Đau thượng vị sau ăn',
    presentingComplaint: 'Ợ chua 2 tuần',
    clinicalFindings: 'Ấn đau nhẹ thượng vị',
    primaryDiagnosisCode: 'K29.6',
    primaryDiagnosisName: 'Viêm dạ dày khác',
    clinicalDiagnosis: null,
  },
  versions: [
    {
      versionId: 1,
      resultId: 901,
      versionNo: 1,
      resultText: 'Thực quản thông thoáng. Niêm mạc hang vị xung huyết nhẹ.',
      conclusion: 'Viêm xung huyết hang vị mức độ nhẹ.',
      reportFileUrl: 'https://files.medflow.test/diagnostic/ORD-2026-0415-0142.pdf',
      isFinalized: true,
      enteredByStaffName: 'BS.CKI Hoàng Minh Hải',
      enteredAt: '2026-11-24T09:25:00Z',
      verifiedByStaffName: 'BS.CKI Hoàng Minh Hải',
      verifiedAt: '2026-11-24T09:30:00Z',
      correctionReason: null,
    },
  ],
};

function renderResultViewPage(entry = '/diagnostic/results/ORD-2026-0415-0142/view') {
  useAuthStore.setState({
    user: {
      id: 4,
      email: 'ktv@medflow.test',
      fullName: 'BS.CKI Hoàng Minh Hải',
      roles: ['DIAGNOSTIC_STAFF'],
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
            path="/diagnostic/results/:orderIdOrCode/view"
            element={<DiagnosticResultViewPage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DiagnosticResultViewPage (SCR-DIA-08 real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: SAMPLE_VIEW_DETAIL,
    } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetches GET /diagnostic/results/:idOrCode/view, computes dynamic initials, and renders real result without fake NBI/PACS mocks', async () => {
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);

    renderResultViewPage();

    expect(await screen.findByText('TRẦN MINH QUÂN')).toBeInTheDocument();
    expect(screen.getByText('TQ')).toBeInTheDocument();
    expect(screen.queryByText('NA')).not.toBeInTheDocument();
    expect(screen.queryByText('REC_20260415_1015_0108.mp4')).not.toBeInTheDocument();
    expect(screen.getByText('Viêm xung huyết hang vị mức độ nhẹ.')).toBeInTheDocument();
    expect(screen.getByText(/Cảnh báo dị ứng: Dị ứng Penicillin/i)).toBeInTheDocument();

    fireEvent.click(
      screen.getAllByRole('button', { name: /Tải tài liệu đính kèm|Mở tài liệu/i })[0],
    );
    expect(openSpy).toHaveBeenCalledWith(
      'https://files.medflow.test/diagnostic/ORD-2026-0415-0142.pdf',
      '_blank',
      'noopener,noreferrer',
    );

    fireEvent.click(screen.getByRole('button', { name: /In phiếu kết quả/i }));
    expect(printSpy).toHaveBeenCalled();
  });

  it('renders error alert when Backend returns 404/403 instead of showing mock endoscopy data', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('Không tìm thấy chỉ định CLS'));
    renderResultViewPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('Không tìm thấy chỉ định CLS');
    expect(screen.queryByText('NGUYỄN VĂN AN')).not.toBeInTheDocument();
  });
});
