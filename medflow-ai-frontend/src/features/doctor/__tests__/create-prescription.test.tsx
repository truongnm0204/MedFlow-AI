import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK } from '../data/consultation-workspace-mock';
import { DoctorCreatePrescriptionPage } from '../pages/doctor-create-prescription-page';
import type { ConsultationWorkspaceResponseDto } from '../types/consultation-workspace.types';

function renderPage(entry = '/doctor/consultation/VIS-2026-0212/prescription') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route
            path="/doctor/consultation/:visitIdOrCode/prescription"
            element={<DoctorCreatePrescriptionPage />}
          />
          <Route path="/doctor/prescriptions/new" element={<DoctorCreatePrescriptionPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function authenticateAsDoctor() {
  localStorage.setItem('medflow.access_token', 'jwt-doctor');
  useAuthStore.setState({
    user: {
      id: 12,
      email: 'doctor01@medflow.test',
      fullName: 'BS. CKII Nguyễn Văn Hùng',
      roles: ['DOCTOR'],
      patientCode: null,
    },
    accessToken: 'jwt-doctor',
    refreshToken: 'rtok-doctor',
    isAuthenticated: true,
  });
}

function savedWorkspace(): ConsultationWorkspaceResponseDto {
  return {
    ...CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    actionOutcome: 'PRESCRIPTION_CREATED',
    clinicalContext: {
      ...CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK.clinicalContext,
      prescriptionSummary: {
        totalPrescriptions: 1,
        latestPrescriptionCode: 'RX-202611-0007',
        latestPrescriptionStatus: 'DRAFT',
        latestIssuedAt: null,
        itemCount: 1,
      },
    },
  };
}

describe('DoctorCreatePrescriptionPage (Issue #139 / UC37)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });
  });

  it('loads the workspace and does not call GET /prescription', async () => {
    authenticateAsDoctor();
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    });

    renderPage();

    expect(await screen.findByText('Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText(/Cảnh báo dị ứng: Dị ứng Penicillin/i)).toBeInTheDocument();
    expect(screen.getByText(/I20\.9/)).toBeInTheDocument();
    expect(getSpy).toHaveBeenCalledWith('/doctor/consultation/VIS-2026-0212');
    expect(getSpy.mock.calls.some((call) => String(call[0]).includes('/prescription'))).toBe(false);
  });

  it('saves a DRAFT with PUT and the ADR 0038 body', async () => {
    authenticateAsDoctor();
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    });
    const putSpy = vi.spyOn(apiClient, 'put').mockResolvedValue({ data: savedWorkspace() });

    renderPage();
    await screen.findByText('Nguyễn Văn An');
    fireEvent.change(screen.getByLabelText('Mã thuốc dòng 1'), { target: { value: 'para-500' } });
    fireEvent.change(screen.getByLabelText('Lịch dùng dòng 1'), {
      target: { value: '1 viên sau ăn sáng' },
    });
    fireEvent.change(screen.getByLabelText('Nơi cấp thuốc'), {
      target: { value: 'HOSPITAL_PHARMACY' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu đơn nháp' }));

    await waitFor(() => {
      expect(putSpy).toHaveBeenCalledWith('/doctor/consultation/VIS-2026-0212/prescription', {
        items: [
          {
            medicineCode: 'PARA-500',
            prescribedQuantity: 1,
            doseScheduleText: '1 viên sau ăn sáng',
          },
        ],
        fulfillmentChoice: 'HOSPITAL_PHARMACY',
        expectedStateVersion: 3,
        expectedRevisionNo: 1,
      });
    });
    expect(await screen.findByText(/Đã lưu đơn nháp RX-202611-0007/)).toBeInTheDocument();
    expect(screen.queryByText(/phát hành Đơn thuốc/i)).not.toBeInTheDocument();
  });

  it('shows the 409 detail instead of falling back to a mock prescription', async () => {
    authenticateAsDoctor();
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    });
    vi.spyOn(apiClient, 'put').mockRejectedValue(
      new AxiosError('conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
        status: 409,
        statusText: 'Conflict',
        headers: {},
        config: { headers: {} } as InternalAxiosRequestConfig,
        data: { detail: 'Chưa thể kê đơn thuốc khi chưa ghi nhận chẩn đoán bệnh chính (ICD-10).' },
      }),
    );

    renderPage();
    await screen.findByText('Nguyễn Văn An');
    fireEvent.change(screen.getByLabelText('Mã thuốc dòng 1'), { target: { value: 'PARA-500' } });
    fireEvent.change(screen.getByLabelText('Lịch dùng dòng 1'), {
      target: { value: '1 viên sau ăn sáng' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu đơn nháp' }));

    expect(
      await screen.findByText(
        'Chưa thể kê đơn thuốc khi chưa ghi nhận chẩn đoán bệnh chính (ICD-10).',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/RX-MOCK-0001/)).not.toBeInTheDocument();
  });

  it('hides save when the doctor lacks PRESCRIPTION_ISSUE', async () => {
    authenticateAsDoctor();
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    });

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/doctor/consultation/VIS-2026-0212/prescription']}>
          <Routes>
            <Route
              path="/doctor/consultation/:visitIdOrCode/prescription"
              element={
                <DoctorCreatePrescriptionPage explicitPermissions={['PATIENT_PROFILE_READ']} />
              }
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByText('Nguyễn Văn An')).toBeInTheDocument();
    expect(
      screen.getByText(/không có quyền kê đơn thuốc \(PRESCRIPTION_ISSUE\)/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Lưu đơn nháp' })).not.toBeInTheDocument();
  });

  it('uses the mock workspace when no access token is present and does not call the API', async () => {
    useAuthStore.setState({
      user: {
        id: 12,
        email: 'doctor01@medflow.test',
        fullName: 'BS. CKII Nguyễn Văn Hùng',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: null,
      refreshToken: null,
      isAuthenticated: true,
    });
    const getSpy = vi.spyOn(apiClient, 'get');
    const putSpy = vi.spyOn(apiClient, 'put');

    renderPage();

    expect(await screen.findByText('Nguyễn Văn An')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Mã thuốc dòng 1'), { target: { value: 'PARA-500' } });
    fireEvent.change(screen.getByLabelText('Lịch dùng dòng 1'), {
      target: { value: '1 viên sau ăn sáng' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu đơn nháp' }));

    expect(await screen.findByText(/Đã lưu đơn nháp RX-MOCK-0001/)).toBeInTheDocument();
    expect(getSpy).not.toHaveBeenCalled();
    expect(putSpy).not.toHaveBeenCalled();
  });
});
