import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { PatientDiagnosticResultPage } from '../pages/patient-diagnostic-result-page';

function renderAtOrderRoute(orderCode: string) {
  useAuthStore.setState({
    user: {
      id: 10,
      email: 'patient@medflow.test',
      fullName: 'Nguyễn Văn An',
      roles: ['PATIENT'],
      patientCode: 'PAT-2024-0108',
    },
    accessToken: 'tok',
    refreshToken: 'rtok',
    isAuthenticated: true,
  });

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[`/patient/diagnostic-results/${orderCode}`]}>
        <Routes>
          <Route
            path="/patient/diagnostic-results/:orderCode"
            element={<PatientDiagnosticResultPage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PatientDiagnosticResultPage (ADR-0035 real API)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetches GET /patients/me/diagnostic-results/:orderCode and renders finalized result without mock lab/images', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
      if (url.startsWith('/patients/me/diagnostic-results/')) {
        return {
          data: {
            orderId: 501,
            orderCode: 'CLS-202611-0501',
            visitId: 88,
            visitCode: 'VIS-202611-0088',
            patientId: 10,
            patientCode: 'PAT-2024-0108',
            patientName: 'NGUYỄN VĂN AN',
            gender: 'MALE',
            birthYear: 1988,
            relationshipLabel: 'Bản thân',
            hospitalName: 'BỆNH VIỆN ĐA KHOA MEDFLOW',
            serviceName: 'Nội soi dạ dày tá tràng',
            orderingDoctorName: 'BS.CKI Nguyễn Minh Đức',
            performingStaffName: 'BS.CKII Lê Hoàng Minh',
            performedPlaceName: 'Phòng Nội soi 202',
            signedAt: '2026-11-24T09:30:00Z',
            isPublishedToPatient: true,
            status: 'FINALIZED',
            resultText: 'Niêm mạc hang vị xung huyết nhẹ.\nLỗ môn vị tròn đều.',
            conclusion: 'Viêm xung huyết hang vị mức độ nhẹ.',
            reportFileUrl: 'https://files.medflow.test/reports/CLS-202611-0501.pdf',
            fileName: 'KQ-CLS-202611-0501.pdf',
          },
        } as AxiosResponse;
      }
      return { data: { items: [], unreadCount: 0 } } as AxiosResponse;
    });

    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);

    renderAtOrderRoute('CLS-202611-0501');

    expect(await screen.findByText('BỆNH VIỆN ĐA KHOA MEDFLOW')).toBeInTheDocument();
    expect(getSpy).toHaveBeenCalledWith('/patients/me/diagnostic-results/CLS-202611-0501');
    expect(screen.getByText('PHIẾU KẾT QUẢ CẬN LÂM SÀNG')).toBeInTheDocument();
    expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('Viêm xung huyết hang vị mức độ nhẹ.')).toBeInTheDocument();
    expect(screen.queryByText('Bạch cầu (WBC)')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Tải file PDF/i }));
    expect(openSpy).toHaveBeenCalledWith(
      'https://files.medflow.test/reports/CLS-202611-0501.pdf',
      '_blank',
      'noopener,noreferrer',
    );

    fireEvent.click(screen.getByRole('button', { name: /In phiếu kết quả/i }));
    expect(printSpy).toHaveBeenCalled();
  });

  it('renders unpublished lock screen when isPublishedToPatient is false', async () => {
    vi.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
      if (url.startsWith('/patients/me/diagnostic-results/')) {
        return {
          data: {
            orderId: 502,
            orderCode: 'CLS-202611-0502',
            visitId: 89,
            visitCode: 'VIS-202611-0089',
            patientId: 10,
            patientCode: 'PAT-2024-0108',
            patientName: 'NGUYỄN VĂN AN',
            gender: 'MALE',
            birthYear: 1988,
            relationshipLabel: 'Bản thân',
            hospitalName: 'BỆNH VIỆN ĐA KHOA MEDFLOW',
            serviceName: 'Xét nghiệm sinh hóa máu',
            orderingDoctorName: 'BS.CKI Nguyễn Minh Đức',
            performingStaffName: null,
            performedPlaceName: 'Phòng Xét nghiệm',
            signedAt: null,
            isPublishedToPatient: false,
            status: 'DRAFT',
            resultText: null,
            conclusion: null,
            reportFileUrl: null,
            fileName: null,
          },
        } as AxiosResponse;
      }
      return { data: { items: [], unreadCount: 0 } } as AxiosResponse;
    });

    renderAtOrderRoute('CLS-202611-0502');

    expect(await screen.findByText('Kết quả chưa được công bố cho Bệnh nhân')).toBeInTheDocument();
  });
});
