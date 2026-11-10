import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import {
  CONSULTATION_WORKSPACE_CALLED_MOCK,
  CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
  completeMockVisit,
  createMockDiagnosticOrder,
  openMockConsultationWorkspace,
  recordMockClinicalExamination,
  resolveMockDiagnosticOrderCatalog,
} from '../data/consultation-workspace-mock';
import { DoctorConsultationWorkspacePage } from '../pages/doctor-consultation-workspace-page';
import {
  calculateClinicalBmi,
  completeVisitSchema,
  consultationIdentifierSchema,
  createCompleteVisitSchema,
  createDiagnosticOrderSchema,
  formatBloodPressureLabel,
  isValidIsoCalendarDate,
  openConsultationWorkspaceSchema,
  recordClinicalExaminationSchema,
} from '../schemas/consultation-workspace.schema';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderWithRouter(initialEntry = '/doctor/consultation/VIS-2026-0212') {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <AppRouter />
        <LocationProbe />
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

describe('DoctorConsultationWorkspacePage (Issue #105 / UC31)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });
  });

  it('validates consultationIdentifierSchema and openConsultationWorkspaceSchema', () => {
    expect(consultationIdentifierSchema.safeParse('VIS-2026-0212').success).toBe(true);
    expect(consultationIdentifierSchema.safeParse('q-013').success).toBe(true);
    expect(consultationIdentifierSchema.safeParse('').success).toBe(false);
    expect(
      consultationIdentifierSchema.safeParse('VIS-202610-0001-INVALID-LENGTH-OVER-40-CHARS-LONG')
        .success,
    ).toBe(false);
    expect(consultationIdentifierSchema.safeParse('<script>').success).toBe(false);

    expect(
      openConsultationWorkspaceSchema.safeParse({
        expectedStateVersion: 2,
        notes: 'Bệnh nhân đã vào buồng khám P.102',
      }).success,
    ).toBe(true);
    expect(
      openConsultationWorkspaceSchema.safeParse({
        expectedStateVersion: 0,
      }).success,
    ).toBe(false);
    expect(
      openConsultationWorkspaceSchema.safeParse({
        notes: 'x'.repeat(301),
      }).success,
    ).toBe(false);
    expect(
      openConsultationWorkspaceSchema.safeParse({
        notes: 'Ghi chú có thẻ < unsafe >',
      }).success,
    ).toBe(false);
  });

  it('renders IN_PROGRESS consultation workspace with patient demographics, vitals, ICD-10 diagnoses, queue context, and enabled clinical actions', async () => {
    authenticateAsDoctor();
    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValueOnce({ data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK });

    renderWithRouter('/doctor/consultation/VIS-2026-0212');

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/doctor/consultation/VIS-2026-0212');
    });

    expect(await screen.findByRole('heading', { name: 'Nguyễn Văn An' })).toBeInTheDocument();
    expect(screen.getByText('BN-2026-0212')).toBeInTheDocument();
    expect(
      screen.getAllByText('Dị ứng Penicillin (nổi mề đay), tiền sử tăng huyết áp 5 năm').length,
    ).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('145/90 mmHg')).toBeInTheDocument();
    expect(screen.getByText('88 bpm')).toBeInTheDocument();
    expect(screen.getByText('36.8 °C')).toBeInTheDocument();
    expect(screen.getByText('97%')).toBeInTheDocument();
    expect(screen.getByText('24.2 kg/m²')).toBeInTheDocument();
    expect(screen.getByText('I20.9')).toBeInTheDocument();
    expect(screen.getByText('Cơn đau thắt ngực, không đặc hiệu')).toBeInTheDocument();
    expect(screen.getByText('Bệnh án nháp (DRAFT)')).toBeInTheDocument();
    expect(screen.getByText('Cho phép ghi nhận lâm sàng')).toBeInTheDocument();
    expect(screen.getByText('6/7 khả dụng')).toBeInTheDocument();
  });

  it('renders CALLED workspace in read-only mode, validates notes input, and transitions to IN_PROGRESS on POST /doctor/consultation/:idOrCode/open', async () => {
    authenticateAsDoctor();
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: CONSULTATION_WORKSPACE_CALLED_MOCK,
    });

    const openedResponse = openMockConsultationWorkspace('VIS-2026-0213', {
      expectedStateVersion: 2,
      notes: 'Mời bệnh nhân vào bàn khám số 1',
    });
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: openedResponse,
    });

    renderWithRouter('/doctor/consultation/VIS-2026-0213');

    expect(await screen.findByRole('heading', { name: 'Trần Thị Bích' })).toBeInTheDocument();
    expect(screen.getByText('Đã gọi vào phòng')).toBeInTheDocument();
    expect(screen.getByText('Chưa khởi tạo bệnh án')).toBeInTheDocument();

    const notesInput = screen.getByLabelText('Ghi chú mở phiên khám');
    const openBtn = screen.getByRole('button', { name: /Bắt đầu khám lâm sàng/ });

    // Client validation rejects '<' or '>'
    fireEvent.change(notesInput, { target: { value: 'Ghi chú <script>' } });
    fireEvent.click(openBtn);

    expect(await screen.findByText('Ghi chú không được chứa ký tự < hoặc >.')).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();

    // Valid notes submit
    fireEvent.change(notesInput, {
      target: { value: 'Mời bệnh nhân vào bàn khám số 1' },
    });
    fireEvent.click(openBtn);

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith('/doctor/consultation/VIS-2026-0213/open', {
        expectedStateVersion: 2,
        notes: 'Mời bệnh nhân vào bàn khám số 1',
      });
    });

    expect(
      await screen.findByText(/Đã mở phiên khám lâm sàng cho bệnh nhân Trần Thị Bích/),
    ).toBeInTheDocument();
    expect(screen.getByText('Bệnh án nháp (DRAFT)')).toBeInTheDocument();
    expect(screen.getByText('v3')).toBeInTheDocument();
  });

  it('renders AWAITING_RESULTS and COMPLETED states with readOnlyReason and disabled write actions', async () => {
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
    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    const { unmount } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/doctor/consultation/VIS-2026-0217']}>
          <AppRouter />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByRole('heading', { name: 'Đỗ Minh Tuấn' })).toBeInTheDocument();
    expect(screen.getByText('Chờ 1 kết quả CLS')).toBeInTheDocument();
    expect(
      screen.getAllByText(
        /Chưa thể kết luận chẩn đoán khi kết quả cận lâm sàng bắt buộc chưa hoàn tất/,
      ).length,
    ).toBeGreaterThanOrEqual(1);
    unmount();

    renderWithRouter('/doctor/consultation/VIS-2026-0209');
    expect(await screen.findByRole('heading', { name: 'Ngô Thị Tuyết' })).toBeInTheDocument();
    expect(screen.getByText('Đã khóa bệnh án (FINALIZED)')).toBeInTheDocument();
    expect(screen.getByText('RX-20261107-009')).toBeInTheDocument();
  });

  it('surfaces HTTP 409 Conflict ProblemDetails on open without masking with mock data and supports retry on GET error', async () => {
    authenticateAsDoctor();

    const notFoundError = new AxiosError('nf', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 404,
      statusText: 'Not Found',
      headers: {},
      config: { headers: {} } as InternalAxiosRequestConfig,
      data: { detail: 'Không tìm thấy lượt khám VIS-2026-9999.' },
    });

    vi.spyOn(apiClient, 'get')
      .mockRejectedValueOnce(notFoundError)
      .mockResolvedValueOnce({ data: CONSULTATION_WORKSPACE_CALLED_MOCK });

    renderWithRouter('/doctor/consultation/VIS-2026-0213');

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Không tìm thấy lượt khám VIS-2026-9999.',
      );
    });

    fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));

    expect(await screen.findByRole('heading', { name: 'Trần Thị Bích' })).toBeInTheDocument();

    const conflictError = new AxiosError('conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 409,
      statusText: 'Conflict',
      headers: {},
      config: { headers: {} } as InternalAxiosRequestConfig,
      data: {
        detail:
          'Điểm dịch vụ/phòng khám hiện đang có bệnh nhân khác trong trạng thái đang khám (IN_PROGRESS).',
      },
    });

    vi.spyOn(apiClient, 'post').mockRejectedValueOnce(conflictError);

    fireEvent.click(screen.getByRole('button', { name: /Bắt đầu khám lâm sàng/ }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Điểm dịch vụ/phòng khám hiện đang có bệnh nhân khác trong trạng thái đang khám (IN_PROGRESS).',
      );
    });
  });

  it('enforces RequireAuth / RequireDoctor on /doctor/consultation and navigates from Assigned Queue row action', async () => {
    // 1. Unauthenticated user redirected to /login
    const { unmount } = renderWithRouter('/doctor/consultation/VIS-2026-0213');
    expect(screen.getByTestId('location-probe')).toHaveTextContent(
      '/login?returnUrl=%2Fdoctor%2Fconsultation%2FVIS-2026-0213',
    );
    unmount();

    // 2. Non-doctor role redirected away from /doctor/consultation
    useAuthStore.setState({
      user: {
        id: 20,
        email: 'reception@medflow.test',
        fullName: 'Lễ tân A',
        roles: ['RECEPTION'],
        patientCode: null,
      },
      accessToken: 'jwt-rec',
      refreshToken: 'rtok-rec',
      isAuthenticated: true,
    });
    const { unmount: unmount2 } = renderWithRouter('/doctor/consultation/VIS-2026-0213');
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/reception');
    unmount2();

    // 3. Authenticated Doctor navigates from /doctor/assigned-queue "Mở bàn khám A013" -> /doctor/consultation/VIS-2026-0213
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

    renderWithRouter('/doctor/assigned-queue');
    const openBtn = await screen.findByRole('button', { name: 'Mở bàn khám A013' });
    fireEvent.click(openBtn);

    await waitFor(() => {
      expect(screen.getByTestId('location-probe')).toHaveTextContent(
        '/doctor/consultation/VIS-2026-0213',
      );
      expect(
        screen.getByRole('heading', { name: 'Không gian khám lâm sàng (UC31)' }),
      ).toBeInTheDocument();
    });
  });

  it('validates lookup bar input before navigating to another visit code', async () => {
    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/doctor/consultation/VIS-2026-0212']}>
          <DoctorConsultationWorkspacePage />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    await screen.findByRole('heading', { name: 'Nguyễn Văn An' });

    const searchInput = screen.getByLabelText('Tra cứu mã lượt khám');
    fireEvent.change(searchInput, { target: { value: 'INVALID CODE WITH SPACES!' } });
    fireEvent.click(screen.getByRole('button', { name: 'Mở ca khám' }));

    expect(
      await screen.findByText(
        'Mã lượt khám chỉ được chứa chữ cái, chữ số, dấu #, gạch ngang (-) hoặc gạch dưới (_).',
      ),
    ).toBeInTheDocument();

    fireEvent.change(searchInput, { target: { value: 'VIS-2026-0213' } });
    fireEvent.click(screen.getByRole('button', { name: 'Mở ca khám' }));

    await waitFor(() => {
      expect(screen.getByTestId('location-probe')).toHaveTextContent(
        '/doctor/consultation/VIS-2026-0213',
      );
    });
  });

  it('validates recordClinicalExaminationSchema rules (BP pairing, systolic > diastolic, vital ranges, text limits, and non-empty guard)', () => {
    expect(calculateClinicalBmi(170, 68)).toBe(23.5);
    expect(formatBloodPressureLabel(130, 82)).toBe('130/82 mmHg');

    // Valid full payload
    expect(
      recordClinicalExaminationSchema.safeParse({
        presentingComplaint: 'Đau ngực trái lan lên vai',
        symptomHistory: 'Khởi phát cách đây 3 ngày',
        relevantHistoryAllergies: 'Dị ứng Penicillin',
        vitalSigns: {
          bpSystolicMmhg: 130,
          bpDiastolicMmhg: 82,
          pulseBpm: 76,
          temperatureC: 36.8,
          spo2Percent: 98,
          heightCm: 170,
          weightKg: 68,
        },
        clinicalFindings: 'Tim đều, phổi trong',
        clinicalConclusion: 'Theo dõi đau thắt ngực ổn định',
        amendmentReason: 'Cập nhật sinh hiệu lần 2',
        isConfirmed: true,
        expectedStateVersion: 3,
        expectedRevisionNo: 1,
      }).success,
    ).toBe(true);

    // Empty payload rejected
    const emptyRes = recordClinicalExaminationSchema.safeParse({});
    expect(emptyRes.success).toBe(false);
    if (!emptyRes.success) {
      expect(emptyRes.error.issues[0]?.message).toBe(
        'Vui lòng nhập ít nhất một thông tin triệu chứng, dấu hiệu sinh tồn hoặc nhận xét khám lâm sàng.',
      );
    }

    // Unpaired BP rejected
    const unpairedBp = recordClinicalExaminationSchema.safeParse({
      vitalSigns: { bpSystolicMmhg: 125, bpDiastolicMmhg: null },
    });
    expect(unpairedBp.success).toBe(false);
    if (!unpairedBp.success) {
      expect(unpairedBp.error.issues[0]?.message).toBe(
        'Huyết áp tâm thu và huyết áp tâm trương phải được nhập cùng nhau.',
      );
    }

    // Systolic <= Diastolic rejected
    const invertedBp = recordClinicalExaminationSchema.safeParse({
      vitalSigns: { bpSystolicMmhg: 85, bpDiastolicMmhg: 90 },
    });
    expect(invertedBp.success).toBe(false);
    if (!invertedBp.success) {
      expect(invertedBp.error.issues[0]?.message).toBe(
        'Huyết áp tâm thu phải lớn hơn huyết áp tâm trương.',
      );
    }

    // Out-of-range vital sign rejected
    expect(
      recordClinicalExaminationSchema.safeParse({
        vitalSigns: { spo2Percent: 45 },
      }).success,
    ).toBe(false);

    // Unsafe HTML characters rejected
    expect(
      recordClinicalExaminationSchema.safeParse({
        clinicalFindings: 'Khám thực thể <script>alert(1)</script>',
      }).success,
    ).toBe(false);
  });

  it('records clinical examination (UC33 / #109) with live BMI/BP preview and updates workspace revision #2', async () => {
    authenticateAsDoctor();
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    });

    const recordedResponse = recordMockClinicalExamination('VIS-2026-0212', {
      presentingComplaint: 'Đau tức ngực trái giảm sau nghỉ ngơi',
      symptomHistory: 'Đau âm ỉ sau xương ức 3 ngày, không lan xuống cánh tay.',
      relevantHistoryAllergies: 'Dị ứng Penicillin và Ibuprofen',
      vitalSigns: {
        bpSystolicMmhg: 130,
        bpDiastolicMmhg: 82,
        pulseBpm: 76,
        temperatureC: 36.7,
        spo2Percent: 98,
        heightCm: 170,
        weightKg: 68,
      },
      clinicalFindings: 'Tim đều 76 lần/phút, phổi thông khí rõ hai bên, bụng mềm.',
      clinicalConclusion: 'Cơn đau thắt ngực ổn định, đáp ứng tốt với nghỉ ngơi.',
      amendmentReason: 'Bổ sung sinh hiệu đo lại sau nghỉ 10 phút',
      isConfirmed: true,
      expectedStateVersion: 3,
      expectedRevisionNo: 1,
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: recordedResponse,
    });

    renderWithRouter('/doctor/consultation/VIS-2026-0212');

    expect(await screen.findByRole('heading', { name: 'Nguyễn Văn An' })).toBeInTheDocument();

    // Open form via UC33 button
    fireEvent.click(
      screen.getByRole('button', { name: /Ghi nhận \/ Cập nhật khám lâm sàng \(UC33\)/ }),
    );

    expect(
      screen.getByRole('form', { name: 'Biểu mẫu ghi nhận sinh hiệu và khám lâm sàng' }),
    ).toBeInTheDocument();

    // Update vital signs and verify live preview
    fireEvent.change(screen.getByLabelText('Huyết áp tâm thu (mmHg)'), {
      target: { value: '130' },
    });
    fireEvent.change(screen.getByLabelText('Huyết áp tâm trương (mmHg)'), {
      target: { value: '82' },
    });
    fireEvent.change(screen.getByLabelText('Mạch (lần/phút)'), {
      target: { value: '76' },
    });
    fireEvent.change(screen.getByLabelText('Nhiệt độ (°C)'), {
      target: { value: '36.7' },
    });
    fireEvent.change(screen.getByLabelText('SpO2 (%)'), {
      target: { value: '98' },
    });
    fireEvent.change(screen.getByLabelText('Chiều cao (cm)'), {
      target: { value: '170' },
    });
    fireEvent.change(screen.getByLabelText('Cân nặng (kg)'), {
      target: { value: '68' },
    });

    // Live preview updates immediately before submit
    expect(screen.getByText('130/82 mmHg')).toBeInTheDocument();
    expect(screen.getByText('23.5 kg/m²')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Lý do đến khám / Triệu chứng chính'), {
      target: { value: 'Đau tức ngực trái giảm sau nghỉ ngơi' },
    });
    fireEvent.change(screen.getByLabelText('Tiền sử & Dị ứng liên quan'), {
      target: { value: 'Dị ứng Penicillin và Ibuprofen' },
    });
    fireEvent.change(screen.getByLabelText('Khám lâm sàng tổng quát & Chuyên khoa'), {
      target: { value: 'Tim đều 76 lần/phút, phổi thông khí rõ hai bên, bụng mềm.' },
    });
    fireEvent.change(screen.getByLabelText('Nhận định & Kết luận lâm sàng ban đầu'), {
      target: { value: 'Cơn đau thắt ngực ổn định, đáp ứng tốt với nghỉ ngơi.' },
    });
    fireEvent.change(screen.getByLabelText('Lý do cập nhật / Ghi chú phiên bản'), {
      target: { value: 'Bổ sung sinh hiệu đo lại sau nghỉ 10 phút' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Lưu khám lâm sàng \(UC33\)/ }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/doctor/consultation/VIS-2026-0212/clinical-examination',
        expect.objectContaining({
          presentingComplaint: 'Đau tức ngực trái giảm sau nghỉ ngơi',
          relevantHistoryAllergies: 'Dị ứng Penicillin và Ibuprofen',
          vitalSigns: {
            bpSystolicMmhg: 130,
            bpDiastolicMmhg: 82,
            pulseBpm: 76,
            temperatureC: 36.7,
            spo2Percent: 98,
            heightCm: 170,
            weightKg: 68,
          },
          clinicalFindings: 'Tim đều 76 lần/phút, phổi thông khí rõ hai bên, bụng mềm.',
          clinicalConclusion: 'Cơn đau thắt ngực ổn định, đáp ứng tốt với nghỉ ngơi.',
          amendmentReason: 'Bổ sung sinh hiệu đo lại sau nghỉ 10 phút',
          isConfirmed: true,
          expectedStateVersion: 3,
          expectedRevisionNo: 1,
        }),
      );
    });

    expect(
      await screen.findByText(
        /Đã ghi nhận sinh hiệu và khám lâm sàng cho bệnh nhân Nguyễn Văn An \(VIS-2026-0212\)/,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText(/Mã BA #801 • Phiên bản #2/)).toBeInTheDocument();
    expect(screen.getAllByText('Dị ứng Penicillin và Ibuprofen').length).toBeGreaterThanOrEqual(1);
  });

  it('blocks invalid UC33 form input and surfaces HTTP 409 Conflict on revision_no mismatch', async () => {
    authenticateAsDoctor();
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    });

    const conflictError = new AxiosError('conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 409,
      statusText: 'Conflict',
      headers: {},
      config: { headers: {} } as InternalAxiosRequestConfig,
      data: {
        detail:
          'Hồ sơ khám lâm sàng đã có phiên bản mới hơn (revision_no mismatch). Vui lòng tải lại trước khi lưu.',
      },
    });
    const postSpy = vi.spyOn(apiClient, 'post').mockRejectedValueOnce(conflictError);

    renderWithRouter('/doctor/consultation/VIS-2026-0212/clinical-examination');

    expect(await screen.findByRole('heading', { name: 'Nguyễn Văn An' })).toBeInTheDocument();
    // Because route ends with /clinical-examination, form opens automatically
    expect(
      screen.getByRole('form', { name: 'Biểu mẫu ghi nhận sinh hiệu và khám lâm sàng' }),
    ).toBeInTheDocument();

    // 1. Enter unpaired blood pressure -> blocked client-side
    fireEvent.change(screen.getByLabelText('Huyết áp tâm trương (mmHg)'), {
      target: { value: '' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Lưu khám lâm sàng \(UC33\)/ }));

    expect(
      await screen.findByText('Huyết áp tâm thu và huyết áp tâm trương phải được nhập cùng nhau.'),
    ).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();

    // 2. Restore diastolic and submit -> surfaces HTTP 409 Conflict from backend
    fireEvent.change(screen.getByLabelText('Huyết áp tâm trương (mmHg)'), {
      target: { value: '90' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Lưu khám lâm sàng \(UC33\)/ }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Hồ sơ khám lâm sàng đã có phiên bản mới hơn (revision_no mismatch). Vui lòng tải lại trước khi lưu.',
      );
    });
  });

  it('hides UC33 write controls when doctor lacks MEDICAL_RECORD_WRITE permission', async () => {
    authenticateAsDoctor();
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    });

    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/doctor/consultation/VIS-2026-0212']}>
          <DoctorConsultationWorkspacePage explicitPermissions={['PATIENT_PROFILE_READ']} />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByRole('heading', { name: 'Nguyễn Văn An' })).toBeInTheDocument();
    expect(screen.getByText('Thiếu quyền MEDICAL_RECORD_WRITE')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Ghi nhận \/ Cập nhật khám lâm sàng \(UC33\)/ }),
    ).not.toBeInTheDocument();
    expect(
      screen.getAllByText(
        'Tài khoản chưa được cấp quyền ghi nhận hồ sơ bệnh án (MEDICAL_RECORD_WRITE).',
      ).length,
    ).toBeGreaterThanOrEqual(1);
  });

  it('validates createDiagnosticOrderSchema rules (1..10 items, duplicate guard, priority enum, instruction/clinicalNote limits, and HTML char guard)', () => {
    // Valid multi-item payload
    const validMulti = createDiagnosticOrderSchema.safeParse({
      items: [
        {
          serviceId: 301,
          serviceCode: 'CLS-XN-001',
          priority: 'URGENT',
          requiredForConclusion: true,
          instruction: 'Lấy máu tĩnh mạch ống EDTA',
        },
        {
          serviceId: 303,
          serviceCode: 'CLS-CDHA-001',
          priority: 'ROUTINE',
          requiredForConclusion: false,
          instruction: 'Chụp tư thế đứng thẳng',
        },
      ],
      clinicalNote: 'Theo dõi hội chứng vành cấp',
      transitionToAwaitingResults: false,
      expectedStateVersion: 3,
    });
    expect(validMulti.success).toBe(true);
    if (validMulti.success) {
      expect(validMulti.data.items?.[1]?.priority).toBe('NORMAL');
    }

    // Empty payload rejected
    const emptyOrder = createDiagnosticOrderSchema.safeParse({});
    expect(emptyOrder.success).toBe(false);
    if (!emptyOrder.success) {
      expect(emptyOrder.error.issues[0]?.message).toBe(
        'Vui lòng chọn ít nhất một dịch vụ cận lâm sàng để chỉ định.',
      );
    }

    // Duplicate serviceId rejected
    const duplicateOrder = createDiagnosticOrderSchema.safeParse({
      items: [
        { serviceId: 301, serviceCode: 'CLS-XN-001', priority: 'NORMAL' },
        { serviceId: 301, serviceCode: 'CLS-XN-001', priority: 'URGENT' },
      ],
    });
    expect(duplicateOrder.success).toBe(false);
    if (!duplicateOrder.success) {
      expect(duplicateOrder.error.issues[0]?.message).toBe(
        'Không được chọn trùng lặp dịch vụ cận lâm sàng trong cùng một lần chỉ định.',
      );
    }

    // Over 10 items rejected
    const overLimitOrder = createDiagnosticOrderSchema.safeParse({
      items: Array.from({ length: 11 }, (_, idx) => ({
        serviceId: 300 + idx + 1,
        serviceCode: `CLS-XN-0${idx + 1}`,
      })),
    });
    expect(overLimitOrder.success).toBe(false);
    if (!overLimitOrder.success) {
      expect(overLimitOrder.error.issues[0]?.message).toBe(
        'Mỗi lần chỉ định tối đa 10 dịch vụ cận lâm sàng.',
      );
    }

    // Invalid priority rejected
    expect(
      createDiagnosticOrderSchema.safeParse({
        items: [{ serviceId: 301, priority: 'SUPER_FAST' }],
      }).success,
    ).toBe(false);

    // Unsafe HTML characters in instruction or clinicalNote rejected
    expect(
      createDiagnosticOrderSchema.safeParse({
        items: [{ serviceId: 301, instruction: 'Chỉ dẫn <script>' }],
      }).success,
    ).toBe(false);
    expect(
      createDiagnosticOrderSchema.safeParse({
        items: [{ serviceId: 301 }],
        clinicalNote: 'Ghi chú <unsafe>',
      }).success,
    ).toBe(false);
  });

  it('creates diagnostic orders (UC34 / #119) for laboratory and imaging services, assigns queue tickets, and locks conclusion actions while mandatory results are pending', async () => {
    authenticateAsDoctor();
    const catalogMock = resolveMockDiagnosticOrderCatalog('VIS-2026-0212');
    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValueOnce({ data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK })
      .mockResolvedValueOnce({ data: catalogMock });

    const createdWorkspace = createMockDiagnosticOrder('VIS-2026-0212', {
      items: [
        {
          serviceId: 302,
          serviceCode: 'CLS-XN-002',
          priority: 'STAT',
          requiredForConclusion: true,
          instruction: 'Cấp cứu lấy máu định lượng Troponin T hs ngay',
        },
        {
          serviceId: 303,
          serviceCode: 'CLS-CDHA-001',
          priority: 'URGENT',
          requiredForConclusion: true,
          instruction: 'Chụp X-quang tim phổi thẳng tại giường/buồng 204',
        },
      ],
      clinicalNote: 'Nghi ngờ hội chứng mạch vành cấp',
      transitionToAwaitingResults: false,
      expectedStateVersion: 3,
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: createdWorkspace,
    });

    renderWithRouter('/doctor/consultation/VIS-2026-0212');

    expect(await screen.findByRole('heading', { name: 'Nguyễn Văn An' })).toBeInTheDocument();

    // Open UC34 form
    fireEvent.click(screen.getByRole('button', { name: /Chỉ định cận lâm sàng \(UC34\)/ }));

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/doctor/consultation/VIS-2026-0212/diagnostic-orders');
    });

    expect(
      await screen.findByRole('form', { name: 'Biểu mẫu chỉ định cận lâm sàng' }),
    ).toBeInTheDocument();

    // Unauthorized service is disabled in catalog
    const unauthorizedBtn = screen.getByRole('button', {
      name: 'Chọn dịch vụ Chụp cắt lớp vi tính động mạch vành (MSCT)',
    });
    expect(unauthorizedBtn).toBeDisabled();
    expect(screen.getByText('Chưa phân quyền BS')).toBeInTheDocument();

    // Select Laboratory service (CLS-XN-002) and Imaging service (CLS-CDHA-001)
    fireEvent.click(
      screen.getByRole('button', { name: 'Chọn dịch vụ Định lượng Troponin T hs (Máu)' }),
    );
    fireEvent.click(
      screen.getByRole('button', { name: 'Chọn dịch vụ Chụp X-quang ngực thẳng kỹ thuật số' }),
    );

    expect(screen.getByText('Dịch vụ đã chọn trong phiếu (2/10)')).toBeInTheDocument();

    // Configure priority and instructions
    fireEvent.change(screen.getByLabelText('Mức độ ưu tiên CLS-XN-002'), {
      target: { value: 'STAT' },
    });
    fireEvent.change(screen.getByLabelText('Chỉ dẫn cận lâm sàng CLS-XN-002'), {
      target: { value: 'Cấp cứu lấy máu định lượng Troponin T hs ngay' },
    });
    fireEvent.change(screen.getByLabelText('Mức độ ưu tiên CLS-CDHA-001'), {
      target: { value: 'URGENT' },
    });
    fireEvent.change(screen.getByLabelText('Chỉ dẫn cận lâm sàng CLS-CDHA-001'), {
      target: { value: 'Chụp X-quang tim phổi thẳng tại giường/buồng 204' },
    });
    fireEvent.change(screen.getByLabelText('Ghi chú lâm sàng chỉ định'), {
      target: { value: 'Nghi ngờ hội chứng mạch vành cấp' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Tạo phiếu chỉ định CLS \(UC34\)/ }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/doctor/consultation/VIS-2026-0212/diagnostic-orders',
        expect.objectContaining({
          items: [
            {
              serviceId: 302,
              serviceCode: 'CLS-XN-002',
              priority: 'STAT',
              requiredForConclusion: true,
              instruction: 'Cấp cứu lấy máu định lượng Troponin T hs ngay',
            },
            {
              serviceId: 303,
              serviceCode: 'CLS-CDHA-001',
              priority: 'URGENT',
              requiredForConclusion: true,
              instruction: 'Chụp X-quang tim phổi thẳng tại giường/buồng 204',
            },
          ],
          clinicalNote: 'Nghi ngờ hội chứng mạch vành cấp',
          transitionToAwaitingResults: false,
          expectedStateVersion: 3,
        }),
      );
    });

    expect(
      await screen.findByText(
        /Đã tạo phiếu chỉ định cận lâm sàng và xếp hàng đợi CLS cho bệnh nhân Nguyễn Văn An \(VIS-2026-0212\)/,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText('DIA-202611-0511')).toBeInTheDocument();
    expect(screen.getByText('Số CLS: C001')).toBeInTheDocument();
    expect(screen.getByText('Số CLS: C002')).toBeInTheDocument();
    expect(screen.getByText('Chờ 2 kết quả CLS')).toBeInTheDocument();
  });

  it('auto-opens UC34 form on /diagnostic-orders route, blocks empty/unsafe client input, and surfaces HTTP 409 Conflict from backend', async () => {
    authenticateAsDoctor();
    const catalogMock = resolveMockDiagnosticOrderCatalog('VIS-2026-0212');
    vi.spyOn(apiClient, 'get')
      .mockResolvedValueOnce({ data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK })
      .mockResolvedValueOnce({ data: catalogMock });

    const conflictError = new AxiosError('conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 409,
      statusText: 'Conflict',
      headers: {},
      config: { headers: {} } as InternalAxiosRequestConfig,
      data: {
        detail:
          "Dịch vụ cận lâm sàng 'Tổng phân tích tế bào máu ngoại vi (CBC)' (CLS-XN-001) đã có phiếu chỉ định đang chờ xử lý trong lượt khám này.",
      },
    });
    const postSpy = vi.spyOn(apiClient, 'post').mockRejectedValueOnce(conflictError);

    renderWithRouter('/doctor/consultation/VIS-2026-0212/diagnostic-orders');

    expect(
      await screen.findByRole('form', { name: 'Biểu mẫu chỉ định cận lâm sàng' }),
    ).toBeInTheDocument();

    // 1. Submit with 0 selected services -> blocked client-side
    fireEvent.click(screen.getByRole('button', { name: /Tạo phiếu chỉ định CLS \(UC34\)/ }));
    expect(
      await screen.findByText('Vui lòng chọn ít nhất một dịch vụ cận lâm sàng để chỉ định.'),
    ).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();

    // 2. Select CBC service and submit -> surfaces HTTP 409 Conflict from backend
    fireEvent.click(
      await screen.findByRole('button', {
        name: 'Chọn dịch vụ Tổng phân tích tế bào máu ngoại vi (CBC)',
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: /Tạo phiếu chỉ định CLS \(UC34\)/ }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        "Dịch vụ cận lâm sàng 'Tổng phân tích tế bào máu ngoại vi (CBC)' (CLS-XN-001) đã có phiếu chỉ định đang chờ xử lý trong lượt khám này.",
      );
    });
    // Draft selection is preserved when submission fails
    expect(screen.getByText('Dịch vụ đã chọn trong phiếu (1/10)')).toBeInTheDocument();
  });

  it('hides UC34 diagnostic order controls when doctor lacks DIAGNOSTIC_ORDER permission', async () => {
    authenticateAsDoctor();
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    });

    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/doctor/consultation/VIS-2026-0212']}>
          <DoctorConsultationWorkspacePage explicitPermissions={['MEDICAL_RECORD_WRITE']} />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByRole('heading', { name: 'Nguyễn Văn An' })).toBeInTheDocument();
    expect(screen.getByText('Thiếu quyền DIAGNOSTIC_ORDER')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Chỉ định cận lâm sàng \(UC34\)/ }),
    ).not.toBeInTheDocument();
    expect(
      screen.getAllByText('Tài khoản chưa được cấp quyền chỉ định cận lâm sàng (DIAGNOSTIC_ORDER).')
        .length,
    ).toBeGreaterThanOrEqual(1);
  });

  it('validates completeVisitSchema rules (clinicalConclusion bounds, text field limits, HTML char guard, followUpDate future check, and version bounds)', () => {
    expect(isValidIsoCalendarDate('2026-11-14')).toBe(true);
    expect(isValidIsoCalendarDate('2026-02-29')).toBe(false);
    expect(isValidIsoCalendarDate('14-11-2026')).toBe(false);

    const schemaWithRef = createCompleteVisitSchema('2026-11-07');

    // Valid full payload with future followUpDate
    expect(
      schemaWithRef.safeParse({
        clinicalConclusion: 'Cơn đau thắt ngực ổn định, điều trị ngoại trú theo phác đồ.',
        dischargeInstructions: 'Uống thuốc đúng giờ, hạn chế gắng sức nặng và giảm muối.',
        followUpInstructions: 'Tái khám chuyên khoa Tim mạch sau 7 ngày kèm xét nghiệm lipid máu.',
        followUpDate: '2026-11-14',
        warningSigns: 'Đau ngực kéo dài trên 15 phút, khó thở, vã mồ hôi lạnh.',
        completionNote: 'Đã tư vấn kỹ cho bệnh nhân và người nhà.',
        confirmNoPrescription: false,
        expectedStateVersion: 3,
        expectedRevisionNo: 1,
      }).success,
    ).toBe(true);

    // Minimal / empty payload is valid (backend uses existing clinicalConclusion if omitted)
    expect(completeVisitSchema.safeParse({}).success).toBe(true);

    // clinicalConclusion < 3 chars when non-empty rejected
    const shortConclusion = schemaWithRef.safeParse({
      clinicalConclusion: 'AB',
    });
    expect(shortConclusion.success).toBe(false);
    if (!shortConclusion.success) {
      expect(shortConclusion.error.issues[0]?.message).toBe(
        'Kết luận lâm sàng phải có ít nhất 3 ký tự.',
      );
    }

    // clinicalConclusion > 2000 chars rejected
    expect(
      schemaWithRef.safeParse({
        clinicalConclusion: 'a'.repeat(2001),
      }).success,
    ).toBe(false);

    // Unsafe HTML characters rejected across fields
    expect(
      schemaWithRef.safeParse({
        clinicalConclusion: 'Kết luận <script>alert(1)</script>',
      }).success,
    ).toBe(false);
    expect(
      schemaWithRef.safeParse({
        dischargeInstructions: 'Hướng dẫn <b>unsafe</b>',
      }).success,
    ).toBe(false);

    // Invalid ISO calendar date rejected
    const invalidDate = schemaWithRef.safeParse({
      followUpDate: '2026-02-30',
    });
    expect(invalidDate.success).toBe(false);
    if (!invalidDate.success) {
      expect(invalidDate.error.issues[0]?.message).toBe(
        'Ngày hẹn tái khám không hợp lệ (định dạng YYYY-MM-DD).',
      );
    }

    // followUpDate <= reference date rejected
    const sameDayDate = schemaWithRef.safeParse({
      followUpDate: '2026-11-07',
    });
    expect(sameDayDate.success).toBe(false);
    if (!sameDayDate.success) {
      expect(sameDayDate.error.issues[0]?.message).toBe(
        'Ngày hẹn tái khám phải sau ngày khám hiện tại.',
      );
    }

    // expectedStateVersion / expectedRevisionNo < 1 rejected
    expect(
      schemaWithRef.safeParse({
        expectedStateVersion: 0,
      }).success,
    ).toBe(false);
    expect(
      schemaWithRef.safeParse({
        expectedRevisionNo: 0,
      }).success,
    ).toBe(false);
  });

  it('completes visit (UC38 / #117) on POST /doctor/consultation/:idOrCode/complete, locks medical record (FINALIZED), confirms diagnoses, and disables write actions', async () => {
    authenticateAsDoctor();
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    });

    const completedResponse = completeMockVisit('VIS-2026-0212', {
      clinicalConclusion:
        'Cơn đau thắt ngực ổn định trên nền tăng huyết áp, đáp ứng điều trị nội khoa.',
      dischargeInstructions: 'Dùng thuốc đều đặn mỗi sáng, kiêng rượu bia và thức ăn mặn.',
      followUpInstructions: 'Tái khám phòng 102 sau 2 tuần để kiểm tra huyết áp và điện tim.',
      followUpDate: '2099-12-31',
      warningSigns: 'Đau ngực dữ dội lan lên cổ hàm, khó thở cấp.',
      completionNote: 'Bệnh nhân ổn định, đồng ý phác đồ ngoại trú.',
      confirmNoPrescription: false,
      expectedStateVersion: 3,
      expectedRevisionNo: 1,
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: completedResponse,
    });

    renderWithRouter('/doctor/consultation/VIS-2026-0212');

    expect(await screen.findByRole('heading', { name: 'Nguyễn Văn An' })).toBeInTheDocument();
    expect(screen.getByText('Đủ điều kiện hoàn tất')).toBeInTheDocument();

    // Open UC38 form
    fireEvent.click(screen.getByRole('button', { name: /Hoàn tất lượt khám \(UC38\)/ }));

    expect(screen.getByRole('form', { name: 'Biểu mẫu hoàn tất lượt khám' })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Kết luận lâm sàng cuối cùng'), {
      target: {
        value: 'Cơn đau thắt ngực ổn định trên nền tăng huyết áp, đáp ứng điều trị nội khoa.',
      },
    });
    fireEvent.change(screen.getByLabelText('Hướng dẫn điều trị / dặn dò xuất viện'), {
      target: { value: 'Dùng thuốc đều đặn mỗi sáng, kiêng rượu bia và thức ăn mặn.' },
    });
    fireEvent.change(screen.getByLabelText('Hướng dẫn tái khám'), {
      target: { value: 'Tái khám phòng 102 sau 2 tuần để kiểm tra huyết áp và điện tim.' },
    });
    fireEvent.change(screen.getByLabelText('Ngày hẹn tái khám'), {
      target: { value: '2099-12-31' },
    });
    fireEvent.change(screen.getByLabelText('Dấu hiệu cảnh báo cần tái khám ngay'), {
      target: { value: 'Đau ngực dữ dội lan lên cổ hàm, khó thở cấp.' },
    });
    fireEvent.change(screen.getByLabelText('Ghi chú hoàn tất lượt khám'), {
      target: { value: 'Bệnh nhân ổn định, đồng ý phác đồ ngoại trú.' },
    });

    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận hoàn tất & Khóa bệnh án \(UC38\)/ }),
    );

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/doctor/consultation/VIS-2026-0212/complete',
        expect.objectContaining({
          clinicalConclusion:
            'Cơn đau thắt ngực ổn định trên nền tăng huyết áp, đáp ứng điều trị nội khoa.',
          dischargeInstructions: 'Dùng thuốc đều đặn mỗi sáng, kiêng rượu bia và thức ăn mặn.',
          followUpInstructions: 'Tái khám phòng 102 sau 2 tuần để kiểm tra huyết áp và điện tim.',
          followUpDate: '2099-12-31',
          warningSigns: 'Đau ngực dữ dội lan lên cổ hàm, khó thở cấp.',
          completionNote: 'Bệnh nhân ổn định, đồng ý phác đồ ngoại trú.',
          confirmNoPrescription: false,
          expectedStateVersion: 3,
          expectedRevisionNo: 1,
        }),
      );
    });

    expect(
      await screen.findByText(
        /Đã hoàn tất lượt khám và khóa hồ sơ bệnh án \(FINALIZED\) cho bệnh nhân Nguyễn Văn An \(VIS-2026-0212\)/,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText('Hồ sơ bệnh án đã khóa hoàn tất (FINALIZED)')).toBeInTheDocument();
    expect(screen.getByText('Đã khóa bệnh án (FINALIZED)')).toBeInTheDocument();
    expect(screen.getAllByText('Chẩn đoán xác định').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('1/7 khả dụng')).toBeInTheDocument();
  });

  it('auto-opens UC38 form on /complete route, blocks invalid client input, and surfaces HTTP 409 Conflict while preserving draft state', async () => {
    authenticateAsDoctor();
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    });

    const conflictError = new AxiosError('conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 409,
      statusText: 'Conflict',
      headers: {},
      config: { headers: {} } as InternalAxiosRequestConfig,
      data: {
        detail:
          'Trạng thái hàng đợi hoặc phiên bản bệnh án đã thay đổi bởi thao tác khác. Vui lòng tải lại trước khi hoàn tất lượt khám.',
      },
    });
    const postSpy = vi.spyOn(apiClient, 'post').mockRejectedValueOnce(conflictError);

    renderWithRouter('/doctor/consultation/VIS-2026-0212/complete');

    expect(
      await screen.findByRole('form', { name: 'Biểu mẫu hoàn tất lượt khám' }),
    ).toBeInTheDocument();

    // 1. Enter past follow-up date -> blocked client-side without calling API
    fireEvent.change(screen.getByLabelText('Ngày hẹn tái khám'), {
      target: { value: '2020-01-01' },
    });
    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận hoàn tất & Khóa bệnh án \(UC38\)/ }),
    );

    expect(
      await screen.findByText('Ngày hẹn tái khám phải sau ngày khám hiện tại.'),
    ).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();

    // 2. Fix follow-up date, enter discharge instructions, and submit -> surfaces HTTP 409 Conflict
    fireEvent.change(screen.getByLabelText('Ngày hẹn tái khám'), {
      target: { value: '2099-12-31' },
    });
    fireEvent.change(screen.getByLabelText('Hướng dẫn điều trị / dặn dò xuất viện'), {
      target: { value: 'Theo dõi huyết áp tại nhà ngày 2 lần' },
    });
    fireEvent.click(
      screen.getByRole('button', { name: /Xác nhận hoàn tất & Khóa bệnh án \(UC38\)/ }),
    );

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Trạng thái hàng đợi hoặc phiên bản bệnh án đã thay đổi bởi thao tác khác. Vui lòng tải lại trước khi hoàn tất lượt khám.',
      );
    });

    // Draft input is preserved after error
    expect(screen.getByLabelText('Hướng dẫn điều trị / dặn dò xuất viện')).toHaveValue(
      'Theo dõi huyết áp tại nhà ngày 2 lần',
    );
  });

  it('hides UC38 complete visit controls when doctor lacks MEDICAL_RECORD_WRITE permission', async () => {
    authenticateAsDoctor();
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    });

    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/doctor/consultation/VIS-2026-0212']}>
          <DoctorConsultationWorkspacePage explicitPermissions={['DIAGNOSTIC_ORDER']} />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByRole('heading', { name: 'Nguyễn Văn An' })).toBeInTheDocument();
    expect(screen.getByText('Khóa quyền MEDICAL_RECORD_WRITE')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Hoàn tất lượt khám \(UC38\)/ }),
    ).not.toBeInTheDocument();
    expect(
      screen.getAllByText(
        'Tài khoản chưa được cấp quyền ghi nhận hồ sơ bệnh án (MEDICAL_RECORD_WRITE).',
      ).length,
    ).toBeGreaterThanOrEqual(2);
  });
});
