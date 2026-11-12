import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { AxiosError, type AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { DIAGNOSTIC_SERVICES_MOCK } from '../data/diagnostic-services-data';
import { INITIAL_SERVICES } from '../data/examination-services-data';
import { DiagnosticServiceListPage } from '../pages/diagnostic-service-list-page';
import { ExaminationServiceListPage } from '../pages/examination-service-list-page';
import { useExaminationServiceStore } from '../store/examination-service.store';

function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
}

function renderDiagnosticPage() {
  const qc = createTestQueryClient();
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/admin/diagnostic-services']}>
        <DiagnosticServiceListPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DiagnosticServiceListPage (SCR-ADM-DSV-01)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    useAuthStore.getState().setAuth({
      user: {
        id: 11,
        email: 'hospital_admin01@medflow.test',
        fullName: 'TS. BS Vũ Quốc Tuấn',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
        staffCode: 'STF-009',
      },
      accessToken: 'jwt-hospital-admin-token',
      refreshToken: 'jwt-refresh-token',
    });
    useExaminationServiceStore.setState({
      services: [...INITIAL_SERVICES],
      searchQuery: '',
      categoryFilter: '',
      departmentFilter: '',
      bookingFilter: '',
      statusFilter: '',
      page: 1,
      pageSize: 10,
      selectedIds: [],
      isColumnModalOpen: false,
      priceHistoryService: null,
      updatePriceService: null,
    });
  });

  it('gọi GET /admin/diagnostic-services với page=1, pageSize=10 và hiển thị đúng metrics cùng dữ liệu bảng', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: DIAGNOSTIC_SERVICES_MOCK,
    });

    renderDiagnosticPage();

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith(
        '/admin/diagnostic-services',
        expect.objectContaining({
          params: expect.objectContaining({ page: 1, pageSize: 10 }),
        }),
      );
      expect(screen.getByText('DIA-XRAY')).toBeInTheDocument();
    });

    const totalCard = screen.getByText('Tổng dịch vụ CLS').closest('div')!;
    expect(within(totalCard).getByText('6')).toBeInTheDocument();

    const gluRow = screen.getByText('DIA-GLU').closest('tr')!;
    expect(within(gluRow).getByText('Chưa có giá')).toBeInTheDocument();

    const hpRow = screen.getByText('DIA-HP').closest('tr')!;
    expect(within(hpRow).getByText('Ngừng hoạt động')).toBeInTheDocument();
    expect(within(hpRow).getByText('Chưa gán điểm phục vụ')).toBeInTheDocument();
  });

  it('lọc theo trạng thái INACTIVE và hiển thị đủ 3 khoa phòng trong dropdown', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: DIAGNOSTIC_SERVICES_MOCK,
    });

    renderDiagnosticPage();

    await waitFor(() => {
      expect(screen.getByText('DIA-XRAY')).toBeInTheDocument();
    });

    const departmentSelect = screen.getByLabelText('Lọc theo khoa phòng');
    await waitFor(() => {
      const options = within(departmentSelect)
        .getAllByRole('option')
        .map((opt) => opt.textContent);
      expect(options).toEqual([
        'Tất cả khoa phòng',
        'Khoa Chẩn đoán hình ảnh',
        'Khoa Thăm dò chức năng',
        'Khoa Xét nghiệm',
      ]);
    });

    const statusSelect = screen.getByLabelText('Lọc theo trạng thái');
    fireEvent.change(statusSelect, { target: { value: 'INACTIVE' } });

    await waitFor(() => {
      const lastCall = getSpy.mock.calls.at(-1);
      expect(lastCall?.[0]).toBe('/admin/diagnostic-services');
      expect(lastCall?.[1]).toEqual(
        expect.objectContaining({
          params: expect.objectContaining({ status: 'INACTIVE', page: 1 }),
        }),
      );
    });
  });

  it('chặn từ khóa dài hơn 100 ký tự và không gọi API với từ khóa không hợp lệ', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: DIAGNOSTIC_SERVICES_MOCK,
    });

    renderDiagnosticPage();

    await waitFor(() => {
      expect(screen.getByText('DIA-XRAY')).toBeInTheDocument();
    });

    const longKeyword = 'a'.repeat(101);
    const keywordInput = screen.getByPlaceholderText('Tìm theo mã, tên hoặc mô tả dịch vụ');
    fireEvent.change(keywordInput, { target: { value: longKeyword } });
    fireEvent.submit(screen.getByRole('search'));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Từ khóa tìm kiếm tối đa 100 ký tự.',
    );

    const hasLongKeywordCall = getSpy.mock.calls.some(
      (call) =>
        (call[1] as { params?: { keyword?: string } } | undefined)?.params?.keyword === longKeyword,
    );
    expect(hasLongKeywordCall).toBe(false);
  });

  it('hiển thị lỗi ProblemDetails 403 và cho phép thử lại', async () => {
    const forbiddenError = new AxiosError('Forbidden', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 403,
      data: {
        title: 'Forbidden',
        detail: 'Bạn không có quyền quản trị cơ sở này.',
      },
    } as AxiosResponse);

    vi.spyOn(apiClient, 'get')
      .mockRejectedValueOnce(forbiddenError)
      .mockResolvedValue({ data: DIAGNOSTIC_SERVICES_MOCK });

    renderDiagnosticPage();

    expect(await screen.findByText('Bạn không có quyền quản trị cơ sở này.')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Thử lại/i }));

    await waitFor(() => {
      expect(screen.getByText('DIA-XRAY')).toBeInTheDocument();
    });
  });

  it('hiển thị trạng thái rỗng khi lọc không có kết quả và cho phép xoá bộ lọc', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockImplementation(async (_url, config) => {
      const params = (config as { params?: { status?: string } } | undefined)?.params;
      if (params?.status === 'INACTIVE') {
        return {
          data: {
            ...DIAGNOSTIC_SERVICES_MOCK,
            data: [],
            total: 0,
            totalPages: 0,
          },
        };
      }
      return { data: DIAGNOSTIC_SERVICES_MOCK };
    });

    renderDiagnosticPage();

    await waitFor(() => {
      expect(screen.getByText('DIA-XRAY')).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText('Lọc theo trạng thái'), {
      target: { value: 'INACTIVE' },
    });

    expect(
      await screen.findByText('Không tìm thấy dịch vụ cận lâm sàng phù hợp bộ lọc.'),
    ).toBeInTheDocument();

    vi.spyOn(Date, 'now').mockReturnValue(Date.now() + 60_000);
    fireEvent.click(screen.getByRole('button', { name: 'Xoá bộ lọc' }));

    await waitFor(() => {
      const lastCall = getSpy.mock.calls.at(-1);
      const lastParams = (lastCall?.[1] as { params?: { status?: string } } | undefined)?.params;
      expect(lastParams?.status).toBeUndefined();
      expect(screen.getByText('DIA-XRAY')).toBeInTheDocument();
    });
  });

  it('lọc dữ liệu mock theo từ khóa khi chạy ở chế độ test không dùng spy', async () => {
    renderDiagnosticPage();

    await waitFor(() => {
      expect(screen.getByText('DIA-CBC')).toBeInTheDocument();
      expect(screen.getByText('DIA-XRAY')).toBeInTheDocument();
    });

    const keywordInput = screen.getByPlaceholderText('Tìm theo mã, tên hoặc mô tả dịch vụ');
    fireEvent.change(keywordInput, { target: { value: 'xray' } });
    fireEvent.submit(screen.getByRole('search'));

    await waitFor(() => {
      expect(screen.getByText('DIA-XRAY')).toBeInTheDocument();
      expect(screen.queryByText('DIA-CBC')).not.toBeInTheDocument();
    });
  });

  it('chặn SYSTEM_ADMIN ở route /admin/diagnostic-services và ẩn tab Dịch vụ cận lâm sàng với SYSTEM_ADMIN', () => {
    useAuthStore.getState().setAuth({
      user: {
        id: 1,
        email: 'sysadmin@medflow.test',
        fullName: 'Quản trị hệ thống',
        roles: ['SYSTEM_ADMIN'],
        patientCode: null,
        staffCode: 'SYS-001',
      },
      accessToken: 'jwt-sysadmin-token',
      refreshToken: 'jwt-refresh-token',
    });

    const qc = createTestQueryClient();
    const { unmount } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/diagnostic-services']}>
          <AppRouter />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(
      screen.queryByRole('heading', { name: 'Danh mục Dịch vụ Cận lâm sàng' }),
    ).not.toBeInTheDocument();
    unmount();

    const { unmount: unmountExamSys } = render(
      <MemoryRouter initialEntries={['/admin/services']}>
        <ExaminationServiceListPage />
      </MemoryRouter>,
    );

    expect(screen.queryByRole('link', { name: /Dịch vụ cận lâm sàng/i })).not.toBeInTheDocument();
    unmountExamSys();

    useAuthStore.getState().setAuth({
      user: {
        id: 11,
        email: 'hospital_admin01@medflow.test',
        fullName: 'TS. BS Vũ Quốc Tuấn',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
        staffCode: 'STF-009',
      },
      accessToken: 'jwt-hospital-admin-token',
      refreshToken: 'jwt-refresh-token',
    });

    render(
      <MemoryRouter initialEntries={['/admin/services']}>
        <ExaminationServiceListPage />
      </MemoryRouter>,
    );

    const diagLink = screen.getByRole('link', { name: /Dịch vụ cận lâm sàng/i });
    expect(diagLink).toHaveAttribute('href', '/admin/diagnostic-services');
  });
});
