import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK } from '../data/consultation-workspace-mock';
import { DoctorReviewDiagnosticsPage } from '../pages/doctor-review-diagnostics-page';
import type { DoctorReviewDiagnosticsPageProps } from '../pages/doctor-review-diagnostics-page';
import type {
  ConsultationDiagnosticResultItemDto,
  DiagnosticResultsReviewResponseDto,
} from '../types/review-diagnostic.types';

function renderReviewPage(
  props: DoctorReviewDiagnosticsPageProps = {},
  initialEntry = '/doctor/consultation/VIS-202611-0001/diagnostic-results',
) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route
            path="/doctor/consultation/:visitIdOrCode/diagnostic-results"
            element={<DoctorReviewDiagnosticsPage {...props} />}
          />
          <Route
            path="/doctor/diagnostic-results"
            element={<DoctorReviewDiagnosticsPage {...props} />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function buildResultItem(
  overrides: Partial<ConsultationDiagnosticResultItemDto>,
): ConsultationDiagnosticResultItemDto {
  return {
    orderId: 0,
    orderCode: '',
    visitId: 9001,
    visitCode: 'VIS-202611-0001',
    orderingDoctorId: 10,
    orderingDoctorCode: 'DOC-010',
    orderingDoctorName: 'BS. Trần Minh Khoa',
    serviceId: 1,
    serviceCode: '',
    serviceName: '',
    category: 'LABORATORY',
    departmentId: null,
    departmentCode: null,
    departmentName: 'Khoa Nội',
    specialtyId: null,
    specialtyCode: null,
    specialtyName: 'Nội tổng quát',
    unitPrice: 0,
    requiresResultVerification: true,
    priority: 'NORMAL',
    orderStatus: 'ORDERED',
    requiredForConclusion: false,
    instruction: null,
    orderedAt: '2026-11-09T08:40:00',
    confirmedAt: null,
    confirmedByStaffId: null,
    confirmedByStaffCode: null,
    confirmedByStaffName: null,
    queueEntryId: null,
    formattedQueueEntryId: null,
    ticketNo: null,
    queueNumber: null,
    queueStatus: null,
    servicePointId: null,
    servicePointName: null,
    roomId: null,
    roomCode: null,
    roomName: null,
    roomLocation: null,
    completedAt: null,
    hasResult: false,
    isResultReady: false,
    isResultFinalized: false,
    resultId: null,
    resultStatus: 'PENDING',
    resultFinalizedAt: null,
    finalizedByStaffId: null,
    finalizedByStaffCode: null,
    finalizedByStaffName: null,
    latestVersionNo: null,
    resultText: null,
    conclusion: null,
    reportFileUrl: null,
    enteredByStaffId: null,
    enteredByStaffCode: null,
    enteredByStaffName: null,
    enteredAt: null,
    verifiedByStaffId: null,
    verifiedByStaffCode: null,
    verifiedByStaffName: null,
    verifiedAt: null,
    correctionReason: null,
    versions: [],
    ...overrides,
  };
}

const API_FIXTURE: DiagnosticResultsReviewResponseDto = {
  visitId: 9001,
  visitCode: 'VIS-202611-0001',
  visitStatus: 'IN_PROGRESS',
  visitStatusLabel: 'Đang khám',
  workspaceState: 'IN_PROGRESS',
  canViewDiagnosticResults: true,
  canAcknowledgeDiagnosticResults: true,
  disabledReason: null,
  generatedAtUtc: '2026-11-09T03:00:00Z',
  patient: {
    patientId: 501,
    patientCode: 'BN-000501',
    fullName: 'TRẦN VĂN BÌNH',
    dateOfBirth: null,
    birthYear: null,
    age: null,
    gender: null,
    genderLabel: 'Nam',
    phone: null,
    email: null,
    address: null,
    bloodType: null,
    allergiesSummary: null,
  },
  diagnosticSummary: {
    totalQueueEntries: 2,
    openQueueEntries: 1,
    completedQueueEntries: 1,
    allMandatoryResultsReady: true,
    finalResultRequired: true,
  },
  totalOrdersCount: 2,
  readyResultsCount: 1,
  pendingResultsCount: 1,
  mandatoryOrdersCount: 1,
  mandatoryReadyCount: 1,
  mandatoryPendingCount: 0,
  items: [
    buildResultItem({
      orderId: 701,
      orderCode: 'DIA-202611-0701',
      serviceCode: 'LAB-CBC',
      serviceName: 'Công thức máu tổng quát',
      orderStatus: 'COMPLETED',
      requiredForConclusion: true,
      hasResult: true,
      isResultReady: true,
      isResultFinalized: true,
      resultStatus: 'VERIFIED',
      conclusion: 'Hb 135 g/L',
    }),
    buildResultItem({
      orderId: 702,
      orderCode: 'DIA-202611-0702',
      serviceId: 2,
      serviceCode: 'IMG-ABD-US',
      serviceName: 'Siêu âm ổ bụng',
      category: 'IMAGING',
      orderStatus: 'IN_PROGRESS',
      resultStatus: 'PENDING',
    }),
  ],
};

describe('DoctorReviewDiagnosticsPage (UC35)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    useAuthStore.setState({
      user: {
        id: 3,
        email: 'bacsi@medflow.test',
        fullName: 'BS. CKII Trần Quốc Tuấn',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: null,
      refreshToken: null,
      isAuthenticated: true,
    });
  });

  it('loads the visit diagnostic results and selects the first ready item', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: API_FIXTURE });

    renderReviewPage();

    expect(await screen.findByText('TRẦN VĂN BÌNH')).toBeInTheDocument();
    expect(getSpy).toHaveBeenCalledTimes(1);
    expect(getSpy).toHaveBeenCalledWith('/doctor/consultation/VIS-202611-0001/diagnostic-results');
    expect(screen.getByTestId('review-diagnostic-readiness')).toHaveTextContent(
      'ĐÃ CÓ 1/2 KẾT QUẢ',
    );
    expect(screen.getByTestId('diagnostic-result-card-DIA-202611-0701')).toBeInTheDocument();
    expect(screen.getByTestId('diagnostic-result-card-DIA-202611-0702')).toBeInTheDocument();
    expect(screen.getByTestId('diagnostic-result-detail-panel')).toHaveTextContent('Hb 135 g/L');
  });

  it('shows the not-ready state when a pending result is selected', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: API_FIXTURE });

    renderReviewPage();
    await screen.findByText('TRẦN VĂN BÌNH');

    fireEvent.click(screen.getByRole('button', { name: 'Xem chi tiết phiếu KQ IMG-ABD-US' }));

    expect(screen.getByTestId('diagnostic-result-not-ready')).toBeInTheDocument();
  });

  it('blocks submit when the interpretation contains angle brackets', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: API_FIXTURE });
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValue({ data: {} });

    renderReviewPage();
    await screen.findByText('TRẦN VĂN BÌNH');

    fireEvent.change(screen.getByLabelText('Nhận định kết quả cận lâm sàng (không bắt buộc)'), {
      target: { value: '<b>' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận đã xem xét/ }));

    expect(screen.getByTestId('diagnostic-review-validation-error')).toHaveTextContent(
      'Nhận định kết quả cận lâm sàng không được chứa ký tự < hoặc >.',
    );
    expect(postSpy).not.toHaveBeenCalled();
  });

  it('submits the trimmed interpretation and clears the textarea on success', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: API_FIXTURE });
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValue({
      data: {
        ...CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
        actionOutcome: 'DIAGNOSTIC_RESULTS_REVIEWED',
      },
    });

    renderReviewPage();
    await screen.findByText('TRẦN VĂN BÌNH');

    const textarea = screen.getByLabelText('Nhận định kết quả cận lâm sàng (không bắt buộc)');
    fireEvent.change(textarea, { target: { value: '  Phù hợp lâm sàng  ' } });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận đã xem xét/ }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/doctor/consultation/VIS-202611-0001/diagnostic-results/review',
        { orderIds: [701], clinicalInterpretation: 'Phù hợp lâm sàng' },
      );
    });
    expect(screen.getByTestId('diagnostic-review-success')).toHaveTextContent(
      'Đã ghi nhận xem xét 1 kết quả',
    );
    expect(textarea).toHaveValue('');
  });

  it('surfaces a 409 detail from the review endpoint', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: API_FIXTURE });
    vi.spyOn(apiClient, 'post').mockRejectedValue(
      new AxiosError(
        'Conflict',
        '409',
        undefined,
        {},
        {
          status: 409,
          statusText: 'Conflict',
          headers: {},
          config: {} as never,
          data: { detail: 'Lượt khám hiện đang ở trạng thái CALLED…' },
        },
      ),
    );

    renderReviewPage();
    await screen.findByText('TRẦN VĂN BÌNH');

    fireEvent.click(screen.getByRole('button', { name: /Xác nhận đã xem xét/ }));

    expect(await screen.findByTestId('diagnostic-review-submit-error')).toHaveTextContent(
      'Lượt khám hiện đang ở trạng thái CALLED…',
    );
  });

  it('shows the disabled reason when the doctor cannot acknowledge results', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        ...API_FIXTURE,
        canAcknowledgeDiagnosticResults: false,
        disabledReason: 'Chưa có kết quả cận lâm sàng nào sẵn sàng để bác sĩ xem xét.',
      },
    });

    renderReviewPage();
    await screen.findByText('TRẦN VĂN BÌNH');

    expect(screen.queryByRole('button', { name: /Xác nhận đã xem xét/ })).not.toBeInTheDocument();
    expect(screen.getByTestId('diagnostic-review-disabled-reason')).toHaveTextContent(
      'Chưa có kết quả cận lâm sàng nào sẵn sàng để bác sĩ xem xét.',
    );
  });

  it('hides the review form when the doctor lacks MEDICAL_RECORD_WRITE', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: API_FIXTURE });

    // Empty explicitPermissions falls back to the DOCTOR role, which includes
    // MEDICAL_RECORD_WRITE. A non-empty list without that code is the real denial.
    renderReviewPage({ explicitPermissions: ['PATIENT_PROFILE_READ'] });
    await screen.findByText('TRẦN VĂN BÌNH');

    expect(screen.queryByRole('button', { name: /Xác nhận đã xem xét/ })).not.toBeInTheDocument();
    expect(screen.queryByTestId('diagnostic-review-disabled-reason')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Nhận định kết quả cận lâm sàng (không bắt buộc)')).toBeNull();
  });

  it('surfaces HTTP 403 instead of falling back to mock data', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    vi.spyOn(apiClient, 'get').mockRejectedValue(
      new AxiosError(
        'Forbidden',
        '403',
        undefined,
        {},
        {
          status: 403,
          statusText: 'Forbidden',
          headers: {},
          config: {} as never,
          data: { detail: 'Bạn không có quyền xem dữ liệu này.' },
        },
      ),
    );

    renderReviewPage();

    expect(await screen.findByTestId('review-diagnostic-error-banner')).toHaveTextContent(
      'Bạn không có quyền xem dữ liệu này.',
    );
    expect(screen.queryByText('NGUYỄN VĂN AN')).not.toBeInTheDocument();
  });

  it('falls back to the mock review when no access token is present', async () => {
    renderReviewPage();

    expect(await screen.findByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByTestId('diagnostic-result-card-DIA-202604-9101')).toBeInTheDocument();
  });

  it('shows the no-visit card and does not call the API without a visit code', async () => {
    localStorage.setItem('medflow.access_token', 'tok-doctor');
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: API_FIXTURE });

    renderReviewPage({}, '/doctor/diagnostic-results');

    expect(screen.getByTestId('review-diagnostic-no-visit')).toBeInTheDocument();
    await waitFor(() => {
      expect(getSpy).not.toHaveBeenCalled();
    });
  });
});
