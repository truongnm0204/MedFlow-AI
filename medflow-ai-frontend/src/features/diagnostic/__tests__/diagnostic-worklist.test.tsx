import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { AxiosError, AxiosHeaders } from 'axios';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import {
  DIAGNOSTIC_WORKLIST_ITEMS_MOCK,
  DIAGNOSTIC_WORKLIST_MOCK,
} from '../data/diagnostic-worklist-mock';
import { DiagnosticWorklistPage } from '../pages/diagnostic-worklist-page';
import { diagnosticOrderIdentifierSchema } from '../schemas/diagnostic-worklist-filter.schema';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderWorklistPage(initialEntry = '/diagnostic/worklist') {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/diagnostic/worklist" element={<DiagnosticWorklistPage />} />
          <Route path="/diagnostic/worklist/:orderIdOrCode" element={<DiagnosticWorklistPage />} />
          <Route path="/diagnostic/results/:orderIdOrCode" element={<div>Result View</div>} />
          <Route path="/diagnostic" element={<div>Diagnostic Dashboard</div>} />
        </Routes>
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DiagnosticWorklistPage (UC39 — View Diagnostic Worklist)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('render header, 4 KPI stat cards, service points va giu nguyen thu tu uu tien tu Backend', async () => {
    useAuthStore.setState({
      user: {
        id: 7,
        email: 'diagnostic_staff01@medflow.test',
        fullName: 'KTV. Lê Minh Châu',
        roles: ['DIAGNOSTIC_STAFF'],
        patientCode: null,
        staffCode: 'STF-007',
      },
      accessToken: 'tok-diag',
      refreshToken: 'rtok-diag',
      isAuthenticated: true,
    });

    renderWorklistPage();

    expect(
      await screen.findByText('Danh sách công việc cận lâm sàng (Diagnostic Worklist)'),
    ).toBeInTheDocument();

    expect(screen.getByTestId('worklist-stat-pendingOrders')).toHaveTextContent('2');
    expect(screen.getByTestId('worklist-stat-inQueue')).toHaveTextContent('3');
    expect(screen.getByTestId('worklist-stat-inExecution')).toHaveTextContent('1');
    expect(screen.getByTestId('worklist-stat-completedToday')).toHaveTextContent('1');

    // Service point buckets:
    expect(screen.getByRole('list', { name: 'Điểm thực hiện cận lâm sàng' })).toBeInTheDocument();

    // Verify exact order of rows matches Backend without client-side re-sorting:
    const table = await screen.findByRole('table', {
      name: 'Danh sách công việc cận lâm sàng',
    });
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(6);
    expect(rows[0]).toHaveAttribute('data-testid', 'worklist-row-DIA-202610-0001');
    expect(rows[1]).toHaveAttribute('data-testid', 'worklist-row-DIA-202610-0002');
    expect(rows[2]).toHaveAttribute('data-testid', 'worklist-row-DIA-202610-0003');
    expect(rows[3]).toHaveAttribute('data-testid', 'worklist-row-DIA-202610-0004');
    expect(rows[4]).toHaveAttribute('data-testid', 'worklist-row-DIA-202610-0005');
    expect(rows[5]).toHaveAttribute('data-testid', 'worklist-row-DIA-202610-0006');
  });

  it('loc danh sach theo trang thai, nhom CLS, muc uu tien, diem thuc hien, dieu kien ket luan, tu khoa va dat lai bo loc', async () => {
    useAuthStore.setState({
      user: {
        id: 7,
        email: 'diagnostic_staff01@medflow.test',
        fullName: 'KTV. Lê Minh Châu',
        roles: ['DIAGNOSTIC_STAFF'],
        patientCode: null,
      },
      accessToken: 'tok-diag',
      refreshToken: 'rtok-diag',
      isAuthenticated: true,
    });

    renderWorklistPage();

    await screen.findByTestId('worklist-row-DIA-202610-0001');

    // Filter by status tab "Chờ xác nhận" (ORDERED) -> 2 orders
    const statusGroup = screen.getByRole('group', {
      name: 'Lọc nhanh trạng thái chỉ định CLS',
    });
    fireEvent.click(within(statusGroup).getByRole('button', { name: /Chờ xác nhận/i }));

    await waitFor(() => {
      expect(screen.getByTestId('worklist-row-DIA-202610-0001')).toBeInTheDocument();
      expect(screen.getByTestId('worklist-row-DIA-202610-0002')).toBeInTheDocument();
      expect(screen.queryByTestId('worklist-row-DIA-202610-0003')).not.toBeInTheDocument();
    });

    // Filter by priority STAT -> only DIA-202610-0001
    fireEvent.change(screen.getByLabelText('Lọc mức độ ưu tiên'), {
      target: { value: 'STAT' },
    });

    await waitFor(() => {
      expect(screen.getByTestId('worklist-row-DIA-202610-0001')).toBeInTheDocument();
      expect(screen.queryByTestId('worklist-row-DIA-202610-0002')).not.toBeInTheDocument();
    });

    // Reset filters -> all 6 orders visible again
    fireEvent.click(screen.getByRole('button', { name: /Đặt lại bộ lọc/i }));

    await waitFor(() => {
      expect(screen.getByTestId('worklist-row-DIA-202610-0006')).toBeInTheDocument();
    });

    // Filter by service point card click (sp-003 -> FUNCTIONAL ECG)
    const spList = screen.getByRole('list', { name: 'Điểm thực hiện cận lâm sàng' });
    fireEvent.click(within(spList).getByRole('button', { name: /P\.TDCN-205/i }));

    await waitFor(() => {
      expect(screen.getByTestId('worklist-row-DIA-202610-0002')).toBeInTheDocument();
      expect(screen.queryByTestId('worklist-row-DIA-202610-0001')).not.toBeInTheDocument();
    });

    // Keyword search that yields empty state
    fireEvent.change(screen.getByLabelText('Tìm kiếm danh sách công việc CLS'), {
      target: { value: 'KHONG-CO-TRONG-DANH-SACH-999' },
    });

    expect(
      await screen.findByText('Không tìm thấy chỉ định cận lâm sàng phù hợp với bộ lọc hiện tại.'),
    ).toBeInTheDocument();
  });

  it('kiem tra hop le bo loc va ma phieu bang Zod schema (chan ky tu <script> va do dai > 100)', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: DIAGNOSTIC_WORKLIST_MOCK,
    });

    useAuthStore.getState().setAuth({
      user: {
        id: 7,
        email: 'diagnostic_staff01@medflow.test',
        fullName: 'KTV. Lê Minh Châu',
        roles: ['DIAGNOSTIC_STAFF'],
        patientCode: null,
      },
      accessToken: 'jwt-valid-token',
      refreshToken: 'jwt-refresh-token',
    });

    renderWorklistPage();

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledTimes(1);
    });

    const searchInput = screen.getByLabelText('Tìm kiếm danh sách công việc CLS');
    fireEvent.change(searchInput, {
      target: { value: '<script>alert(1)</script>' },
    });

    expect(
      await screen.findByText('Từ khóa tìm kiếm chứa ký tự không hợp lệ.'),
    ).toBeInTheDocument();
    // Invalid filter must not trigger a new API request:
    expect(getSpy).toHaveBeenCalledTimes(1);

    fireEvent.change(searchInput, {
      target: { value: 'A'.repeat(101) },
    });
    expect(
      await screen.findByText('Từ khóa tìm kiếm không được vượt quá 100 ký tự.'),
    ).toBeInTheDocument();
    expect(getSpy).toHaveBeenCalledTimes(1);

    // Also verify order identifier schema rejects non-positive and unsafe IDs:
    expect(diagnosticOrderIdentifierSchema.safeParse('0').success).toBe(false);
    expect(diagnosticOrderIdentifierSchema.safeParse('-5').success).toBe(false);
    expect(diagnosticOrderIdentifierSchema.safeParse('do-0000').success).toBe(false);
    expect(diagnosticOrderIdentifierSchema.safeParse('DIA<script>').success).toBe(false);
    expect(diagnosticOrderIdentifierSchema.safeParse('#DIA-202610-0001')).toEqual({
      success: true,
      data: 'DIA-202610-0001',
    });
  });

  it('goi GET /diagnostic/worklist kem query params khi co token va hien thi thong tin co so y te tu BE', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        ...DIAGNOSTIC_WORKLIST_MOCK,
        facilityName: 'Bệnh viện Trung tâm MedFlow Sài Gòn',
        departmentName: 'Trung tâm Xét nghiệm & CĐHA Kỹ thuật cao',
        staffProfile: {
          ...DIAGNOSTIC_WORKLIST_MOCK.staffProfile,
          fullName: 'KTV. Nguyễn Hoàng Yến',
          staffCode: 'STF-209',
        },
      },
    });

    useAuthStore.getState().setAuth({
      user: {
        id: 9,
        email: 'yen.ktv@medflow.test',
        fullName: null,
        roles: ['DIAGNOSTIC_STAFF'],
        patientCode: null,
        staffCode: null,
      },
      accessToken: 'jwt-worklist-token',
      refreshToken: 'jwt-worklist-refresh',
    });

    renderWorklistPage();

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/diagnostic/worklist', {
        params: { page: 1, pageSize: 50 },
      });
      expect(screen.getAllByText('KTV. Nguyễn Hoàng Yến').length).toBeGreaterThan(0);
    });

    expect(screen.getByText('Mã NV: STF-209')).toBeInTheDocument();
    expect(screen.getByText(/Bệnh viện Trung tâm MedFlow Sài Gòn/)).toBeInTheDocument();

    // Changing category & mandatory filter sends updated query params:
    fireEvent.change(screen.getByLabelText('Lọc nhóm cận lâm sàng'), {
      target: { value: 'LABORATORY' },
    });
    fireEvent.change(screen.getByLabelText('Lọc điều kiện kết luận'), {
      target: { value: 'REQUIRED' },
    });

    await waitFor(() => {
      expect(getSpy).toHaveBeenLastCalledWith('/diagnostic/worklist', {
        params: {
          page: 1,
          pageSize: 50,
          category: 'LABORATORY',
          requiredForConclusion: true,
        },
      });
    });
  });

  it('mo modal chi tiet khi bam Xem chi tiet hoac truy cap deep-link /diagnostic/worklist/:orderIdOrCode', async () => {
    useAuthStore.setState({
      user: {
        id: 7,
        email: 'diagnostic_staff01@medflow.test',
        fullName: 'KTV. Lê Minh Châu',
        roles: ['DIAGNOSTIC_STAFF'],
        patientCode: null,
      },
      accessToken: 'tok-diag',
      refreshToken: 'rtok-diag',
      isAuthenticated: true,
    });

    renderWorklistPage('/diagnostic/worklist/DIA-202610-0002');

    const dialog = await screen.findByRole('dialog');
    expect(
      await within(dialog).findByText(/Chi tiết chỉ định DIA-202610-0002 \(C022\)/),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(/Chỉ định bổ sung lần #2 • Tiếp nối từ phiếu DIA-202610-0005/),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText('Theo dõi diễn tiến ST chênh sau dùng thuốc'),
    ).toBeInTheDocument();

    // Close modal and open DIA-202610-0001 to verify allergy banner:
    fireEvent.click(within(dialog).getByRole('button', { name: 'Đóng cửa sổ' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Xem chi tiết DIA-202610-0001' }));
    const dialog1 = await screen.findByRole('dialog');
    expect(
      within(dialog1).getByText(/Cảnh báo dị ứng: Dị ứng kháng sinh nhóm Penicillin/),
    ).toBeInTheDocument();
  });

  it('an cac thao tac khong du quyen RBAC va dieu huong sang trang Xem ket qua CLS khi bam Xem ket qua', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        ...DIAGNOSTIC_WORKLIST_MOCK,
        items: DIAGNOSTIC_WORKLIST_ITEMS_MOCK,
        staffProfile: {
          ...DIAGNOSTIC_WORKLIST_MOCK.staffProfile,
          // Staff has no write/confirm/queue permissions:
          permissions: [],
        },
      },
    });

    useAuthStore.getState().setAuth({
      user: {
        id: 7,
        email: 'readonly.cls@medflow.test',
        fullName: 'KTV. Chỉ Xem',
        roles: ['DIAGNOSTIC_STAFF'],
        patientCode: null,
      },
      accessToken: 'jwt-readonly-token',
      refreshToken: 'jwt-readonly-refresh',
    });

    renderWorklistPage();

    await screen.findByTestId('worklist-row-DIA-202610-0001');

    // Write / Queue / Confirm actions must be hidden when staff lacks permissions:
    expect(
      screen.queryByRole('button', { name: /Xác nhận chỉ định DIA-202610-0001/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Tiếp nhận bệnh nhân DIA-202610-0003/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Nhập kết quả DIA-202610-0005/i }),
    ).not.toBeInTheDocument();

    // Viewing completed result (DIA-202610-0006) remains allowed for DIAGNOSTIC_STAFF:
    const viewResultBtn = screen.getByRole('button', {
      name: 'Xem kết quả CLS DIA-202610-0006',
    });
    fireEvent.click(viewResultBtn);

    await waitFor(() => {
      expect(screen.getByTestId('location-probe')).toHaveTextContent(
        '/diagnostic/results/DIA-202610-0006',
      );
    });
  });

  it('khong che giau loi HTTP 403/500 bang mock khi co token va cho phep thu lai (retry)', async () => {
    const forbiddenError = new AxiosError(
      'Request failed with status code 403',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      {
        status: 403,
        statusText: 'Forbidden',
        headers: {},
        config: { headers: new AxiosHeaders() },
        data: {
          detail: 'Tài khoản nhân viên cận lâm sàng chưa được phân công cơ sở y tế hợp lệ.',
        },
      },
    );

    const getSpy = vi
      .spyOn(apiClient, 'get')
      .mockRejectedValueOnce(forbiddenError)
      .mockResolvedValueOnce({ data: DIAGNOSTIC_WORKLIST_MOCK });

    useAuthStore.getState().setAuth({
      user: {
        id: 7,
        email: 'diagnostic_staff01@medflow.test',
        fullName: 'KTV. Lê Minh Châu',
        roles: ['DIAGNOSTIC_STAFF'],
        patientCode: null,
      },
      accessToken: 'jwt-live-token',
      refreshToken: 'jwt-live-refresh',
    });

    renderWorklistPage();

    expect(
      await screen.findByText(
        'Tài khoản nhân viên cận lâm sàng chưa được phân công cơ sở y tế hợp lệ.',
      ),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Thử lại/i }));

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledTimes(2);
      expect(screen.getByTestId('worklist-row-DIA-202610-0001')).toBeInTheDocument();
    });
  });

  it('bao ve route /diagnostic/worklist va /diagnostic/orders bang RequireAuth + RequireDiagnostic', () => {
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/diagnostic/worklist']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByTestId('location-probe')).toHaveTextContent(
      '/login?returnUrl=%2Fdiagnostic%2Fworklist',
    );
    unmount();

    // Authenticated as DOCTOR -> redirected to /doctor when accessing /diagnostic/orders
    useAuthStore.setState({
      user: {
        id: 3,
        email: 'doctor01@medflow.test',
        fullName: 'BS. Trần Khánh',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: 'tok-doc',
      refreshToken: 'rtok-doc',
      isAuthenticated: true,
    });

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/diagnostic/orders']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByTestId('location-probe')).toHaveTextContent('/doctor');
  });
});
