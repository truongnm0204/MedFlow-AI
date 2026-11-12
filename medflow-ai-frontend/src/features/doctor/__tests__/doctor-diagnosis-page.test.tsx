import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { ICD10_CATALOG_MOCK } from '../data/diagnosis-conclusion-mock';
import {
  CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
  recordMockDiagnosis,
} from '../data/consultation-workspace-mock';
import { DoctorDiagnosisPage } from '../pages/doctor-diagnosis-page';
import type {
  ConsultationDiagnosisItemDto,
  ConsultationWorkspaceResponseDto,
  RecordDiagnosisRequest,
} from '../types/consultation-workspace.types';

const ENDOSCOPY_ORDER = {
  orderId: 501,
  orderCode: 'DIA-2026-0501',
  visitId: 212,
  visitCode: 'VIS-2026-0212',
  orderingDoctorId: 1,
  orderingDoctorCode: 'DOC-001',
  orderingDoctorName: 'BS. CKII Trần Quốc Tuấn',
  serviceId: 9,
  serviceCode: 'CLS-NS-001',
  serviceName: 'Nội soi dạ dày',
  category: 'IMAGING' as const,
  unitPrice: 350000,
  requiresResultVerification: true,
  queueEntryId: null,
  formattedQueueEntryId: null,
  ticketNo: null,
  queueNumber: null,
  queueStatus: 'COMPLETED',
  servicePointId: null,
  servicePointName: null,
  roomId: null,
  roomCode: null,
  roomName: null,
  priority: 'NORMAL' as const,
  status: 'COMPLETED' as const,
  requiredForConclusion: true,
  instruction: null,
  orderedAt: '2026-11-07T08:10:00',
  confirmedAt: '2026-11-07T08:20:00',
  isResultFinalized: true,
  conclusion: 'Viêm dạ dày mạn tính thể teo, HP dương tính.',
  resultText: 'Niêm mạc teo.',
};

const BLOCKED_REASON =
  'Chưa thể kết luận chẩn đoán khi kết quả cận lâm sàng bắt buộc chưa hoàn tất.';

const CONCLUSION = 'Viêm dạ dày mạn tính thể teo kèm HP dương tính, điều trị phác đồ 14 ngày.';

function buildFixture(options?: {
  diagnoses?: ConsultationDiagnosisItemDto[];
  recordEnabled?: boolean;
  disabledReason?: string | null;
}): ConsultationWorkspaceResponseDto {
  const base = CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK;
  const recordEnabled = options?.recordEnabled ?? true;
  return {
    ...base,
    clinicalContext: {
      ...base.clinicalContext,
      diagnoses: options?.diagnoses ?? [],
      clinicalConclusion: null,
      diagnosticOrders: [ENDOSCOPY_ORDER],
    },
    allowedActions: base.allowedActions.map((action) =>
      action.id === 'record-diagnosis' || action.ucCode === 'UC36'
        ? {
            ...action,
            enabled: recordEnabled,
            disabledReason: recordEnabled ? null : (options?.disabledReason ?? BLOCKED_REASON),
          }
        : action,
    ),
  };
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

function renderDiagnosisPage(fixture: ConsultationWorkspaceResponseDto) {
  authenticateAsDoctor();
  const getSpy = vi.spyOn(apiClient, 'get').mockImplementation((url: string) => {
    if (url.startsWith('/doctor/icd10-codes')) {
      return Promise.resolve({ data: ICD10_CATALOG_MOCK });
    }
    return Promise.resolve({ data: fixture });
  });
  const postSpy = vi.spyOn(apiClient, 'post').mockImplementation((_url, payload) =>
    Promise.resolve({
      data: recordMockDiagnosis('VIS-2026-0212', payload as RecordDiagnosisRequest),
    }),
  );

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/doctor/consultation/VIS-2026-0212/diagnosis']}>
        <Routes>
          <Route
            path="/doctor/consultation/:visitIdOrCode/diagnosis"
            element={<DoctorDiagnosisPage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );

  return { getSpy, postSpy, fixture };
}

async function chooseIcd(label: RegExp, query: string, optionName: RegExp) {
  const input = screen.getByLabelText(label);
  fireEvent.focus(input);
  fireEvent.change(input, { target: { value: query } });
  fireEvent.click(await screen.findByRole('option', { name: optionName }));
}

describe('DoctorDiagnosisPage (Issue #111 / UC36)', () => {
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

  it('renders the patient and a diagnostic order from the workspace', async () => {
    const { getSpy } = renderDiagnosisPage(buildFixture());

    expect(await screen.findByText('Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText('BN-2026-0212')).toBeInTheDocument();
    expect(screen.getByText('Nội soi dạ dày')).toBeInTheDocument();
    expect(getSpy).toHaveBeenCalledWith('/doctor/consultation/VIS-2026-0212');
  });

  it('blocks confirm when the primary diagnosis and conclusion are empty', async () => {
    const { postSpy } = renderDiagnosisPage(buildFixture());

    fireEvent.click(await screen.findByRole('button', { name: /Xác nhận chẩn đoán & kết luận/i }));

    expect(screen.getByText('Vui lòng chọn chẩn đoán bệnh chính (ICD-10).')).toBeInTheDocument();
    expect(screen.getByText('Kết luận lâm sàng phải có tối thiểu 10 ký tự.')).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();
  });

  it('searches ICD-10, confirms primary and secondary diagnoses, and posts the contract', async () => {
    const { postSpy, fixture } = renderDiagnosisPage(buildFixture());

    await screen.findByRole('button', { name: /Xác nhận chẩn đoán & kết luận/i });
    await chooseIcd(/Chọn bệnh chính \(ICD-10\)/i, 'K29.5', /K29\.5/);
    expect(screen.getByTestId('primary-diagnosis')).toHaveTextContent('K29.5');
    await chooseIcd(/Thêm bệnh kèm theo \(ICD-10\)/i, 'K21.0', /K21\.0/);
    fireEvent.change(screen.getByLabelText(/Kết luận lâm sàng/i), {
      target: { value: CONCLUSION },
    });

    fireEvent.click(screen.getByRole('button', { name: /Xác nhận chẩn đoán & kết luận/i }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/doctor/consultation/VIS-2026-0212/diagnosis',
        expect.objectContaining({
          diagnoses: [
            { code: 'K29.5', isPrimary: true, status: 'CONFIRMED' },
            { code: 'K21.0', isPrimary: false, status: 'CONFIRMED' },
          ],
          diagnosisStatus: 'CONFIRMED',
          isConfirmed: true,
          expectedStateVersion: fixture.queue?.stateVersion,
          expectedRevisionNo: fixture.clinicalContext.currentRevisionNo,
        }),
      );
    });
    expect(
      await screen.findByText(
        'Đã xác nhận chẩn đoán chính K29.5 và kết luận lâm sàng cho lượt khám VIS-2026-0212.',
      ),
    ).toBeInTheDocument();
  });

  it('saves a preliminary draft with an empty conclusion', async () => {
    const { postSpy } = renderDiagnosisPage(buildFixture());

    await screen.findByRole('button', { name: /Lưu nháp/i });
    await chooseIcd(/Chọn bệnh chính \(ICD-10\)/i, 'K29.5', /K29\.5/);
    fireEvent.click(screen.getByRole('button', { name: /Lưu nháp/i }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/doctor/consultation/VIS-2026-0212/diagnosis',
        expect.objectContaining({
          diagnosisStatus: 'PRELIMINARY',
          isConfirmed: false,
          clinicalConclusion: null,
        }),
      );
    });
    expect(
      screen.getByText('Đã lưu nháp chẩn đoán cho lượt khám VIS-2026-0212.'),
    ).toBeInTheDocument();
  });

  it('shows the 409 detail and a reload action', async () => {
    const { postSpy } = renderDiagnosisPage(buildFixture());
    const conflict = new AxiosError('conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 409,
      statusText: 'Conflict',
      headers: {},
      config: { headers: {} } as InternalAxiosRequestConfig,
      data: {
        detail:
          'Hồ sơ khám lâm sàng đã có phiên bản mới hơn (revision_no mismatch). Vui lòng tải lại trước khi lưu.',
      },
    });
    postSpy.mockRejectedValueOnce(conflict);

    await screen.findByRole('button', { name: /Xác nhận chẩn đoán & kết luận/i });
    await chooseIcd(/Chọn bệnh chính \(ICD-10\)/i, 'K29.5', /K29\.5/);
    fireEvent.change(screen.getByLabelText(/Kết luận lâm sàng/i), {
      target: { value: CONCLUSION },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận chẩn đoán & kết luận/i }));

    expect(
      await screen.findByText(
        'Hồ sơ khám lâm sàng đã có phiên bản mới hơn (revision_no mismatch). Vui lòng tải lại trước khi lưu.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Tải lại dữ liệu lượt khám' })).toBeInTheDocument();
  });

  it('hides save actions and shows the disabled reason when UC36 is blocked', async () => {
    renderDiagnosisPage(
      buildFixture({
        recordEnabled: false,
        disabledReason: BLOCKED_REASON,
      }),
    );

    expect(await screen.findByText(BLOCKED_REASON)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Lưu nháp/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Xác nhận chẩn đoán/i })).not.toBeInTheDocument();
  });

  it('prefills an existing preliminary primary diagnosis', async () => {
    const primary: ConsultationDiagnosisItemDto = {
      id: 401,
      icd10CodeId: 4,
      code: 'I10',
      name: 'Tăng huyết áp vô căn (nguyên phát)',
      isPrimary: true,
      clinicalDiagnosis: null,
      status: 'PRELIMINARY',
      confirmedAt: null,
      recordedAt: '2026-11-07T08:30:00',
    };
    renderDiagnosisPage(buildFixture({ diagnoses: [primary] }));

    expect(await screen.findByTestId('primary-diagnosis')).toHaveTextContent('I10');
    expect(screen.getByText('Chẩn đoán sơ bộ (nháp)')).toBeInTheDocument();
  });
});
