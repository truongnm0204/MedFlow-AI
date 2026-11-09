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
  openMockConsultationWorkspace,
} from '../data/consultation-workspace-mock';
import { DoctorConsultationWorkspacePage } from '../pages/doctor-consultation-workspace-page';
import {
  consultationIdentifierSchema,
  openConsultationWorkspaceSchema,
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
});
