import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { DOCTOR_ASSIGNED_QUEUE_MOCK } from '../data/doctor-assigned-queue-mock';
import { PATIENT_MEDICAL_HISTORY_MOCK } from '../data/medical-history-mock';
import { DoctorMedicalHistoryPage } from '../pages/doctor-medical-history-page';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function setDoctorAuth(roles: string[] = ['DOCTOR']) {
  localStorage.setItem('medflow.access_token', 'jwt-doctor');
  useAuthStore.setState({
    user: {
      id: 101,
      email: 'doctor.nam@medflow.vn',
      fullName: 'BS. CKI Nguyễn Hoàng Nam',
      roles,
      patientCode: null,
    },
    accessToken: 'jwt-doctor',
    refreshToken: 'rtok-doctor',
    isAuthenticated: true,
  });
}

function renderWithRoutes(initialPath: string) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route path="/doctor/medical-history" element={<DoctorMedicalHistoryPage />} />
          <Route
            path="/doctor/patients/:patientIdOrCode/medical-history"
            element={<DoctorMedicalHistoryPage />}
          />
          <Route
            path="/doctor/visits/:visitIdOrCode/medical-history"
            element={<DoctorMedicalHistoryPage />}
          />
          <Route
            path="/doctor/consultation/:visitIdOrCode/medical-history"
            element={<DoctorMedicalHistoryPage />}
          />
        </Routes>
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DoctorMedicalHistoryPage (UC32)', () => {
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

  it('renders patient clinical demographics, allergy banner, 4 KPI stat cards, and longitudinal visits', async () => {
    setDoctorAuth();
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: PATIENT_MEDICAL_HISTORY_MOCK });

    renderWithRoutes('/doctor/patients/BN-2026-0212/medical-history');

    expect(await screen.findByText('Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText('Dị ứng Penicillin (phát ban mày đay năm 2021)')).toBeInTheDocument();
    expect(screen.getByText('Nhóm máu: O+')).toBeInTheDocument();
    expect(screen.getByText('Tăng huyết áp độ 2 (I10)')).toBeInTheDocument();

    // 4 KPI Stat Cards
    expect(screen.getByText('Tổng lượt khám')).toBeInTheDocument();
    expect(screen.getAllByText('Bệnh án đã chốt').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Chẩn đoán & Cận lâm sàng')).toBeInTheDocument();
    expect(screen.getByText('Đơn thuốc đã kê')).toBeInTheDocument();

    // Expanded current visit clinical details (vital signs, ICD-10, CLS, prescription)
    expect(screen.getAllByText('145/90 mmHg').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Tăng huyết áp vô căn (nguyên phát)').length).toBeGreaterThanOrEqual(
      1,
    );
    expect(screen.getByText('CLS-0921')).toBeInTheDocument();
    expect(screen.getByText('Điện tâm đồ 12 chuyển đạo thường quy')).toBeInTheDocument();
    expect(screen.getByText('RX-2026-0812')).toBeInTheDocument();
    expect(screen.getByText('Amlodipin 5mg')).toBeInTheDocument();

    // Expand second visit (VIS-2026-0140) to inspect revisions and clinical addenda
    fireEvent.click(screen.getByRole('button', { name: 'Chi tiết lượt khám VIS-2026-0140' }));
    expect(screen.getByText('Bổ sung tư vấn chế độ ăn giảm muối')).toBeInTheDocument();
    fireEvent.click(
      screen.getByRole('button', { name: /Lịch sử phiên bản bệnh án \(2 phiên bản\)/ }),
    );
    expect(
      screen.getAllByText(/Cập nhật kết quả bilan lipid máu vào kết luận/).length,
    ).toBeGreaterThanOrEqual(1);
  });

  it('calls GET /doctor/patients/:id/medical-history in patient mode and GET /doctor/visits/:id/medical-history in visit mode', async () => {
    setDoctorAuth();
    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValue({ data: PATIENT_MEDICAL_HISTORY_MOCK });

    const { unmount } = renderWithRoutes('/doctor/patients/BN-2026-0212/medical-history');

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/doctor/patients/BN-2026-0212/medical-history', {
        params: {
          includeCurrentVisit: true,
          limit: 20,
        },
      });
    });

    unmount();
    getSpy.mockClear();

    renderWithRoutes('/doctor/visits/VIS-2026-0212/medical-history');

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/doctor/visits/VIS-2026-0212/medical-history', {
        params: {
          includeCurrentVisit: true,
          limit: 20,
        },
      });
    });
  });

  it('validates filter form client-side before sending API requests and forwards valid query params', async () => {
    setDoctorAuth();
    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockResolvedValue({ data: PATIENT_MEDICAL_HISTORY_MOCK });

    renderWithRoutes('/doctor/patients/BN-2026-0212/medical-history');

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledTimes(1);
    });

    // 1. Reversed date range (fromDate > toDate)
    fireEvent.change(screen.getByLabelText('Từ ngày'), { target: { value: '2026-11-10' } });
    fireEvent.change(screen.getByLabelText('Đến ngày'), { target: { value: '2026-11-01' } });
    fireEvent.click(screen.getByRole('button', { name: /Áp dụng bộ lọc/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ngày bắt đầu không được lớn hơn ngày kết thúc.',
    );
    expect(getSpy).toHaveBeenCalledTimes(1);

    // 2. Invalid specialty code
    fireEvent.change(screen.getByLabelText('Từ ngày'), { target: { value: '2026-01-01' } });
    fireEvent.change(screen.getByLabelText('Đến ngày'), { target: { value: '2026-11-10' } });
    fireEvent.change(screen.getByLabelText('Lọc theo mã chuyên khoa'), {
      target: { value: 'INVALID SPECIALTY!' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Áp dụng bộ lọc/i }));

    expect(screen.getByRole('alert')).toHaveTextContent('Mã chuyên khoa lọc không hợp lệ.');
    expect(getSpy).toHaveBeenCalledTimes(1);

    // 3. Keyword > 100 chars
    fireEvent.change(screen.getByLabelText('Lọc theo mã chuyên khoa'), {
      target: { value: 'CARDIOLOGY' },
    });
    fireEvent.change(screen.getByLabelText('Tìm kiếm trong lịch sử khám'), {
      target: { value: 'K'.repeat(101) },
    });
    fireEvent.click(screen.getByRole('button', { name: /Áp dụng bộ lọc/i }));

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Từ khóa tìm kiếm không được vượt quá 100 ký tự.',
    );
    expect(getSpy).toHaveBeenCalledTimes(1);

    // 4. Valid filter submission forwards query params to apiClient.get
    fireEvent.change(screen.getByLabelText('Lọc theo trạng thái'), {
      target: { value: 'COMPLETED' },
    });
    fireEvent.change(screen.getByLabelText('Tìm kiếm trong lịch sử khám'), {
      target: { value: 'Amlodipin' },
    });
    fireEvent.click(screen.getByLabelText('Bao gồm lượt khám hiện tại'));
    fireEvent.change(screen.getByLabelText('Số lượng hiển thị'), {
      target: { value: '10' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Áp dụng bộ lọc/i }));

    await waitFor(() => {
      expect(getSpy).toHaveBeenLastCalledWith('/doctor/patients/BN-2026-0212/medical-history', {
        params: {
          status: 'COMPLETED',
          specialtyCode: 'CARDIOLOGY',
          fromDate: '2026-01-01',
          toDate: '2026-11-10',
          keyword: 'Amlodipin',
          includeCurrentVisit: false,
          limit: 10,
        },
      });
    });
  });

  it('surfaces HTTP 403, 404, and 409 ProblemDetails errors in role="alert" without masking with mock data and recovers on retry', async () => {
    setDoctorAuth();

    const forbiddenError = new AxiosError('forbidden', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 403,
      statusText: 'Forbidden',
      headers: {},
      config: { headers: {} } as InternalAxiosRequestConfig,
      data: {
        detail: 'Bác sĩ chỉ được xem lịch sử khám của bệnh nhân thuộc phạm vi phụ trách lâm sàng.',
      },
    });

    const conflictError = new AxiosError('conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 409,
      statusText: 'Conflict',
      headers: {},
      config: { headers: {} } as InternalAxiosRequestConfig,
      data: {
        detail: 'Lượt khám đã bị hủy hoặc vắng mặt, không thể mở lịch sử khám từ lượt khám này.',
      },
    });

    vi.spyOn(apiClient, 'get')
      .mockRejectedValueOnce(forbiddenError)
      .mockRejectedValueOnce(conflictError)
      .mockResolvedValueOnce({ data: PATIENT_MEDICAL_HISTORY_MOCK });

    renderWithRoutes('/doctor/visits/VIS-2026-0212/medical-history');

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Bác sĩ chỉ được xem lịch sử khám của bệnh nhân thuộc phạm vi phụ trách lâm sàng.',
      );
    });
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Lượt khám đã bị hủy hoặc vắng mặt, không thể mở lịch sử khám từ lượt khám này.',
      );
    });

    fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));

    await waitFor(() => {
      expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
    });
  });

  it('hides lookup/filter actions and shows permission warning when user lacks MEDICAL_RECORD_WRITE and PATIENT_PROFILE_READ', () => {
    setDoctorAuth(['SECURITY_AUDITOR']);
    const getSpy = vi.spyOn(apiClient, 'get');

    renderWithRoutes('/doctor/patients/BN-2026-0212/medical-history');

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Tài khoản Bác sĩ hiện tại thiếu quyền truy cập lịch sử khám bệnh lâm sàng (MEDICAL_RECORD_WRITE hoặc PATIENT_PROFILE_READ).',
    );
    expect(screen.queryByRole('button', { name: /Áp dụng bộ lọc/i })).not.toBeInTheDocument();
    expect(getSpy).not.toHaveBeenCalled();
  });

  it('enforces RequireAuth and RequireDoctor on medical history routes and navigates from DoctorAssignedQueuePage', async () => {
    // 1. Unauthenticated -> redirects to /login
    const qc1 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount: unmount1 } = render(
      <QueryClientProvider client={qc1}>
        <MemoryRouter initialEntries={['/doctor/patients/BN-2026-0212/medical-history']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByTestId('location-probe')).toHaveTextContent(
      '/login?returnUrl=%2Fdoctor%2Fpatients%2FBN-2026-0212%2Fmedical-history',
    );
    unmount1();

    // 2. Non-DOCTOR role (RECEPTION) -> redirected to /reception
    setDoctorAuth(['RECEPTION']);
    const qc2 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount: unmount2 } = render(
      <QueryClientProvider client={qc2}>
        <MemoryRouter initialEntries={['/doctor/visits/VIS-2026-0212/medical-history']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByTestId('location-probe')).toHaveTextContent('/reception');
    unmount2();

    // 3. Authenticated DOCTOR navigates from /doctor/assigned-queue row button "Xem lịch sử khám của Nguyễn Văn An (VIS-2026-0212)"
    setDoctorAuth(['DOCTOR']);
    vi.spyOn(apiClient, 'get').mockImplementation((url) => {
      if (url === '/doctor/assigned-queue') {
        return Promise.resolve({ data: DOCTOR_ASSIGNED_QUEUE_MOCK });
      }
      return Promise.resolve({ data: PATIENT_MEDICAL_HISTORY_MOCK });
    });

    const qc3 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc3}>
        <MemoryRouter initialEntries={['/doctor/assigned-queue']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const viewHistoryBtn = await screen.findByRole('button', {
      name: 'Xem lịch sử khám của Nguyễn Văn An (VIS-2026-0212)',
    });
    fireEvent.click(viewHistoryBtn);

    await waitFor(() => {
      expect(screen.getByTestId('location-probe')).toHaveTextContent(
        '/doctor/visits/VIS-2026-0212/medical-history',
      );
    });

    expect(
      await screen.findByRole('heading', { name: 'Lịch sử khám bệnh lâm sàng (UC32)' }),
    ).toBeInTheDocument();
  });
});
