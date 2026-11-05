import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';

import * as systemAdminApi from '../api/system-admin.api';
import { SYSTEM_ADMIN_DASHBOARD_MOCK } from '../data/system-admin-mock-data';
import { SystemAdminDashboardPage } from '../pages/system-admin-dashboard-page';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderPage() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/system-admin']}>
        <SystemAdminDashboardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('SystemAdminDashboardPage (M1-021)', () => {
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

  it('render dashboard header, sidebar dung vai tro System Admin, 4 KPI stat cards, RBAC summary va an chuc nang lam sang trai quyen', async () => {
    useAuthStore.setState({
      user: {
        id: 13,
        email: 'system_admin01@medflow.test',
        fullName: 'KS. Trần Minh Hệ Thống',
        roles: ['SYSTEM_ADMIN'],
        patientCode: null,
        staffCode: 'STF-011',
      },
      accessToken: 'tok-sysadmin',
      refreshToken: 'rtok-sysadmin',
      isAuthenticated: true,
    });

    renderPage();

    expect(
      screen.getByRole('heading', { name: 'Tổng quan quản trị hệ thống' }),
    ).toBeInTheDocument();
    expect(screen.getAllByText('KS. Trần Minh Hệ Thống').length).toBeGreaterThan(0);
    expect(screen.getByText('Mã NV: STF-011')).toBeInTheDocument();
    expect(screen.getByText('SYSTEM ADMIN')).toBeInTheDocument();

    const overviewNavLink = screen.getByRole('link', { name: 'Tổng quan hệ thống' });
    expect(overviewNavLink).toHaveAttribute('aria-current', 'page');
    expect(overviewNavLink).toHaveAttribute('href', '/system-admin');

    await waitFor(() => {
      expect(screen.getByText('Tài khoản toàn hệ thống')).toBeInTheDocument();
    });

    // 4 KPI stat cards
    expect(screen.getByText('Tài khoản toàn hệ thống')).toBeInTheDocument();
    expect(screen.getByText('Tài khoản nhân sự y tế')).toBeInTheDocument();
    expect(screen.getByText('Vai trò & Quyền hạn (RBAC)')).toBeInTheDocument();
    expect(screen.getByText('Nhật ký kiểm toán (24h)')).toBeInTheDocument();

    // 5 lối vào phân hệ được phép của System Admin (UC57–UC60)
    expect(screen.getByText('Quản lý tài khoản nhân sự')).toBeInTheDocument();
    expect(screen.getAllByText('Vai trò & Phân quyền (RBAC)').length).toBeGreaterThan(0);
    expect(screen.getByText('Cấu hình Khoa phòng & Cơ sở')).toBeInTheDocument();
    expect(screen.getByText('Chính sách & Tích hợp hệ thống')).toBeInTheDocument();
    expect(screen.getByText('Nhật ký kiểm toán (Audit Log)')).toBeInTheDocument();

    // RBAC Role Summary card
    expect(screen.getByText('Tổng quan Vai trò & Phân quyền (RBAC)')).toBeInTheDocument();
    expect(screen.getByText('7 vai trò')).toBeInTheDocument();

    // Không hiển thị các chức năng lâm sàng trái quyền
    expect(screen.queryByText('Khám lâm sàng & EMR')).not.toBeInTheDocument();
    expect(screen.queryByText('Kê đơn thuốc điện tử')).not.toBeInTheDocument();
    expect(screen.queryByText('Xác nhận cấp phát')).not.toBeInTheDocument();
  });

  it('chi hien thi cac loi vao phan he duoc cap quyen (permission-based module visibility)', async () => {
    vi.spyOn(systemAdminApi, 'fetchSystemAdminDashboard').mockResolvedValueOnce({
      ...SYSTEM_ADMIN_DASHBOARD_MOCK,
      adminProfile: {
        ...SYSTEM_ADMIN_DASHBOARD_MOCK.adminProfile,
        permissions: ['USER_MANAGE'],
      },
      allowedModules: SYSTEM_ADMIN_DASHBOARD_MOCK.allowedModules,
    });

    useAuthStore.setState({
      user: {
        id: 13,
        email: 'system_admin01@medflow.test',
        fullName: 'KS. Trần Minh Hệ Thống',
        roles: ['SYSTEM_ADMIN'],
        patientCode: null,
        staffCode: 'STF-011',
      },
      accessToken: 'tok-sysadmin',
      refreshToken: 'rtok-sysadmin',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('Quản lý tài khoản nhân sự')).toBeInTheDocument();
    });

    // Hiển thị các phân hệ thuộc quyền USER_MANAGE
    expect(screen.getByText('Quản lý tài khoản nhân sự')).toBeInTheDocument();

    // Ẩn các phân hệ đòi hỏi quyền HOSPITAL_CONFIG hoặc AUDIT_READ
    expect(screen.queryByText('Cấu hình Khoa phòng & Cơ sở')).not.toBeInTheDocument();
    expect(screen.queryByText('Chính sách & Tích hợp hệ thống')).not.toBeInTheDocument();
    expect(screen.queryByText('Nhật ký kiểm toán (Audit Log)')).not.toBeInTheDocument();
  });

  it('kiem tra hop le form bo loc query params (recentAuditLimit 1..50, hospitalId) va goi lai API khi hop le', async () => {
    const fetchSpy = vi
      .spyOn(systemAdminApi, 'fetchSystemAdminDashboard')
      .mockResolvedValue(SYSTEM_ADMIN_DASHBOARD_MOCK);

    useAuthStore.setState({
      user: {
        id: 13,
        email: 'system_admin01@medflow.test',
        fullName: 'KS. Trần Minh Hệ Thống',
        roles: ['SYSTEM_ADMIN'],
        patientCode: null,
      },
      accessToken: 'tok-sysadmin',
      refreshToken: 'rtok-sysadmin',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(fetchSpy).toHaveBeenCalledWith({ recentAuditLimit: 10, hospitalId: null });
    });

    const limitInput = screen.getByLabelText('Số lượng nhật ký hiển thị');
    const hospitalSelect = screen.getByLabelText('Phạm vi cơ sở y tế');
    const applyButton = screen.getByRole('button', { name: /Áp dụng bộ lọc/i });

    // Nhập giá trị không hợp lệ: 0 -> báo lỗi, không gọi thêm API
    fireEvent.change(limitInput, { target: { value: '0' } });
    fireEvent.click(applyButton);

    expect(
      screen.getByText('Số lượng nhật ký hệ thống hiển thị phải từ 1 đến 50.'),
    ).toBeInTheDocument();
    expect(fetchSpy).toHaveBeenCalledTimes(1);

    // Nhập giá trị không hợp lệ: 60 -> báo lỗi
    fireEvent.change(limitInput, { target: { value: '60' } });
    fireEvent.click(applyButton);

    expect(
      screen.getByText('Số lượng nhật ký hệ thống hiển thị phải từ 1 đến 50.'),
    ).toBeInTheDocument();
    expect(fetchSpy).toHaveBeenCalledTimes(1);

    // Nhập giá trị hợp lệ: recentAuditLimit = 5, hospitalId = 1 -> xóa lỗi và gọi API
    fireEvent.change(limitInput, { target: { value: '5' } });
    fireEvent.change(hospitalSelect, { target: { value: '1' } });
    fireEvent.click(applyButton);

    expect(
      screen.queryByText('Số lượng nhật ký hệ thống hiển thị phải từ 1 đến 50.'),
    ).not.toBeInTheDocument();

    await waitFor(() => {
      expect(fetchSpy).toHaveBeenCalledWith({ recentAuditLimit: 5, hospitalId: 1 });
    });
  });

  it('loc nhat ky kiem toan theo phan nhom, tu khoa tim kiem, chon dong xem chi tiet va dat lai bo loc', async () => {
    useAuthStore.setState({
      user: {
        id: 13,
        email: 'system_admin01@medflow.test',
        fullName: 'KS. Trần Minh Hệ Thống',
        roles: ['SYSTEM_ADMIN'],
        patientCode: null,
      },
      accessToken: 'tok-sysadmin',
      refreshToken: 'rtok-sysadmin',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('AUTH_LOGIN_SUCCESS')).toBeInTheDocument();
    });

    // Chọn dòng sự kiện AUD-0903 để cập nhật khung chi tiết
    fireEvent.click(screen.getByText('STAFF_ACCOUNT_CREATE'));
    expect(
      screen.getByText(/Chi tiết sự kiện: AUD-0903 • STAFF_ACCOUNT_CREATE/),
    ).toBeInTheDocument();

    // Lọc theo nhóm "Cấu hình (CONFIG)"
    fireEvent.click(screen.getByRole('button', { name: 'Cấu hình (CONFIG)' }));
    expect(screen.getByText('HOSPITAL_POLICY_UPDATE')).toBeInTheDocument();
    expect(screen.getByText('DEPARTMENT_CREATE')).toBeInTheDocument();
    expect(screen.queryByText('AUTH_LOGIN_SUCCESS')).not.toBeInTheDocument();

    // Đổi về "Tất cả" và tìm kiếm theo từ khóa "REFRESH_REUSE_DETECTED"
    fireEvent.click(screen.getByRole('button', { name: 'Tất cả' }));
    const searchInput = screen.getByLabelText('Tìm kiếm nhật ký kiểm toán');
    fireEvent.change(searchInput, { target: { value: 'REFRESH_REUSE_DETECTED' } });
    expect(screen.getByText('REFRESH_REUSE_DETECTED')).toBeInTheDocument();
    expect(screen.queryByText('AUTH_LOGIN_SUCCESS')).not.toBeInTheDocument();

    // Tìm kiếm từ khóa không tồn tại -> hiển thị empty state + nút Đặt lại bộ lọc
    fireEvent.change(searchInput, { target: { value: 'KHONG-TON-TAI-AUDIT-999' } });
    expect(
      screen.getByText('Không tìm thấy bản ghi nhật ký kiểm toán phù hợp với bộ lọc hiện tại.'),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Đặt lại bộ lọc/i }));
    expect(screen.getByText('AUTH_LOGIN_SUCCESS')).toBeInTheDocument();
    expect(screen.getByText('ROLE_PERMISSION_UPDATE')).toBeInTheDocument();
  });

  it('xu ly trang thai loi API va cho phep thu lai (retry)', async () => {
    vi.spyOn(systemAdminApi, 'fetchSystemAdminDashboard')
      .mockRejectedValueOnce(new Error('Lỗi kết nối máy chủ quản trị hệ thống'))
      .mockResolvedValueOnce(SYSTEM_ADMIN_DASHBOARD_MOCK);

    useAuthStore.setState({
      user: {
        id: 13,
        email: 'system_admin01@medflow.test',
        fullName: 'KS. Trần Minh Hệ Thống',
        roles: ['SYSTEM_ADMIN'],
        patientCode: null,
      },
      accessToken: 'tok-sysadmin',
      refreshToken: 'rtok-sysadmin',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('Lỗi kết nối máy chủ quản trị hệ thống')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /Thử lại/i }));

    await waitFor(() => {
      expect(screen.getByText('Tài khoản toàn hệ thống')).toBeInTheDocument();
    });
  });

  it('bao ve route /system-admin bang RequireAuth va RequireAdmin', () => {
    // 1. Chưa đăng nhập -> redirect /login?returnUrl=%2Fsystem-admin
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc1 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = render(
      <QueryClientProvider client={qc1}>
        <MemoryRouter initialEntries={['/system-admin']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.queryByText('Tổng quan quản trị hệ thống')).not.toBeInTheDocument();
    expect(screen.getByTestId('location-probe')).toHaveTextContent(
      '/login?returnUrl=%2Fsystem-admin',
    );
    unmount();

    // 2. Đã đăng nhập nhưng role PATIENT (không phải Admin) -> redirect về /
    useAuthStore.setState({
      user: {
        id: 10,
        email: 'patient@medflow.test',
        fullName: 'Nguyễn Văn An',
        roles: ['PATIENT'],
        patientCode: 'PAT-001',
      },
      accessToken: 'tok-patient',
      refreshToken: 'rtok-patient',
      isAuthenticated: true,
    });

    const qc2 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc2}>
        <MemoryRouter initialEntries={['/system-admin']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.queryByText('Tổng quan quản trị hệ thống')).not.toBeInTheDocument();
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/');

    // 3. Đã đăng nhập nhưng role HOSPITAL_ADMIN -> RequireSystemAdmin chuyển hướng về /hospital-admin
    useAuthStore.setState({
      user: {
        id: 11,
        email: 'hospital_admin01@medflow.test',
        fullName: 'Quản trị Cơ sở',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
      },
      accessToken: 'tok-hadmin',
      refreshToken: 'rtok-hadmin',
      isAuthenticated: true,
    });

    const qc3 = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc3}>
        <MemoryRouter initialEntries={['/system-admin']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getAllByTestId('location-probe')[1]).toHaveTextContent('/hospital-admin');
  });
});
