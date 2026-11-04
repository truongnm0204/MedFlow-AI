import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { StaffAccountListPage } from '@/features/admin-staff/pages/staff-account-list-page';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { ClinicalSidebar } from '@/shared/components/layout/clinical-sidebar';
import { UserMenu } from '@/shared/components/layout/user-menu';

import { resetMockRolesCatalog } from '../data/role-mock-data';
import { RoleListPage } from '../pages/role-list-page';

function renderWithQueryClient(ui: React.ReactElement, initialRoute = '/admin/roles') {
  const qc = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialRoute]}>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Enforce Role-based Access (M1-085 / Issue #223)', () => {
  beforeEach(() => {
    resetMockRolesCatalog();
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });
  });

  it('hiển thị tab Kiểm tra truy cập trên RoleListPage và đánh giá đúng trạng thái Cho phép / Chặn', async () => {
    renderWithQueryClient(<RoleListPage />);

    const accessCheckTab = screen.getByRole('tab', { name: /Kiểm tra truy cập/i });
    fireEvent.click(accessCheckTab);

    expect(
      screen.getByText(/Bộ kiểm tra thực thi quyền truy cập theo vai trò/i),
    ).toBeInTheDocument();

    // Mặc định: DOCTOR + /doctor + MEDICAL_RECORD_WRITE -> ĐƯỢC PHÉP TRUY CẬP & ĐƯỢC HIỂN THỊ THAO TÁC
    expect(screen.getByText('ĐƯỢC PHÉP TRUY CẬP')).toBeInTheDocument();
    expect(screen.getByText('ĐƯỢC HIỂN THỊ THAO TÁC')).toBeInTheDocument();

    // Đổi sang kiểm tra DOCTOR truy cập /system-admin và quyền USER_MANAGE
    fireEvent.change(screen.getByLabelText('Đường dẫn màn hình kiểm tra'), {
      target: { value: '/system-admin' },
    });
    fireEvent.change(screen.getByLabelText('Quyền hạn thao tác cần kiểm tra'), {
      target: { value: 'USER_MANAGE' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Kiểm tra quyền truy cập/i }));

    await waitFor(() => {
      expect(screen.getByText('CHẶN TRUY CẬP — CHUYỂN HƯỚNG AN TOÀN')).toBeInTheDocument();
      expect(screen.getByText('ẨN THAO TÁC TRÁI QUYỀN')).toBeInTheDocument();
    });
    expect(screen.getByText(/Tự động chuyển hướng về \/doctor/i)).toBeInTheDocument();

    // Đổi vai trò sang SYSTEM_ADMIN -> ĐƯỢC PHÉP TRUY CẬP & ĐƯỢC HIỂN THỊ THAO TÁC
    fireEvent.change(screen.getByLabelText('Chọn vai trò kiểm tra'), {
      target: { value: 'SYSTEM_ADMIN' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Kiểm tra quyền truy cập/i }));

    await waitFor(() => {
      expect(screen.getByText('ĐƯỢC PHÉP TRUY CẬP')).toBeInTheDocument();
      expect(screen.getByText('ĐƯỢC HIỂN THỊ THAO TÁC')).toBeInTheDocument();
    });
  });

  it('kiểm tra hợp lệ dữ liệu biểu mẫu (client validation) khi thiếu vai trò hoặc đường dẫn sai định dạng', () => {
    renderWithQueryClient(<RoleListPage initialView="access-check" />);

    // 1. Bỏ trống vai trò
    fireEvent.change(screen.getByLabelText('Chọn vai trò kiểm tra'), {
      target: { value: '' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Kiểm tra quyền truy cập/i }));

    expect(
      screen.getByText('Vui lòng chọn vai trò cần kiểm tra quyền truy cập.'),
    ).toBeInTheDocument();

    // 2. Chọn lại vai trò nhưng nhập đường dẫn không bắt đầu bằng "/"
    fireEvent.change(screen.getByLabelText('Chọn vai trò kiểm tra'), {
      target: { value: 'RECEPTION' },
    });
    fireEvent.change(screen.getByLabelText('Đường dẫn màn hình kiểm tra'), {
      target: { value: 'admin/roles' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Kiểm tra quyền truy cập/i }));

    expect(
      screen.getByText(
        'Đường dẫn nội bộ phải bắt đầu bằng ký tự "/" hợp lệ (ví dụ: /admin/roles).',
      ),
    ).toBeInTheDocument();

    // 3. Nhấn Đặt lại -> xóa thông báo lỗi và khôi phục trạng thái mặc định
    fireEvent.click(screen.getByRole('button', { name: /Đặt lại/i }));
    expect(
      screen.queryByText(
        'Đường dẫn nội bộ phải bắt đầu bằng ký tự "/" hợp lệ (ví dụ: /admin/roles).',
      ),
    ).not.toBeInTheDocument();
  });

  it('ẩn các nút thao tác quản trị nhân sự (Thêm tài khoản, Ngừng hoạt động) khi đăng nhập bằng HOSPITAL_ADMIN', () => {
    useAuthStore.setState({
      user: {
        id: 20,
        email: 'hospital_admin01@medflow.test',
        fullName: 'Quản trị Bệnh viện',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
        staffCode: 'STF-020',
      },
      accessToken: 'tok-ha',
      refreshToken: 'rtok-ha',
      isAuthenticated: true,
    });

    renderWithQueryClient(<StaffAccountListPage />, '/admin/staff');

    expect(
      screen.getByText(/Chế độ chỉ đọc: Chỉ Quản trị viên hệ thống \(SYSTEM_ADMIN\)/i),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Thêm tài khoản' })).toBeNull();
    expect(screen.queryByRole('button', { name: /Ngừng hoạt động/i })).toBeNull();
  });

  it('ẩn liên kết Cổng Quản trị Cơ sở trong ClinicalSidebar đối với DOCTOR và hiển thị đối với HOSPITAL_ADMIN', () => {
    useAuthStore.setState({
      user: {
        id: 10,
        email: 'doctor01@medflow.test',
        fullName: 'BS Nguyễn Minh Đức',
        roles: ['DOCTOR'],
        patientCode: null,
        staffCode: 'STF-001',
      },
      isAuthenticated: true,
    });

    const { unmount } = renderWithQueryClient(<ClinicalSidebar variant="clinical" />, '/doctor');
    expect(screen.queryByText('Cổng Quản trị Cơ sở')).toBeNull();
    unmount();

    useAuthStore.setState({
      user: {
        id: 20,
        email: 'hospital_admin01@medflow.test',
        fullName: 'Quản trị Cơ sở',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
        staffCode: 'STF-020',
      },
      isAuthenticated: true,
    });

    renderWithQueryClient(<ClinicalSidebar variant="clinical" />, '/doctor');
    expect(screen.getByText('Cổng Quản trị Cơ sở')).toBeInTheDocument();
  });

  it('ẩn các mục chỉ dành cho bệnh nhân trong UserMenu khi đăng nhập bằng tài khoản nhân sự thuần túy (DOCTOR)', () => {
    render(
      <MemoryRouter>
        <UserMenu
          user={{
            fullName: 'BS Nguyễn Minh Đức',
            email: 'doctor01@medflow.test',
            roles: ['DOCTOR'],
            staffCode: 'STF-001',
            patientCode: null,
          }}
        />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByLabelText('Menu tài khoản'));

    expect(screen.getByRole('link', { name: /Bàn làm việc \(Bác sĩ lâm sàng\)/i })).toBeDefined();
    expect(screen.queryByText('Lịch hẹn của tôi')).toBeNull();
    expect(screen.queryByText('Hồ sơ gia đình')).toBeNull();
    expect(screen.getByText('Thông tin cá nhân')).toBeInTheDocument();
  });
});
