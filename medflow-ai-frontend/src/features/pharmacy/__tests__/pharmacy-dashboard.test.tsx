import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import * as pharmacyApi from '../api/pharmacy.api';
import { PHARMACY_DASHBOARD_MOCK } from '../data/pharmacy-mock-data';
import { PharmacyDashboardPage } from '../pages/pharmacy-dashboard-page';

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
      <MemoryRouter initialEntries={['/pharmacy']}>
        <PharmacyDashboardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PharmacyDashboardPage', () => {
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

  it('render dashboard header, sidebar dung vai tro Duoc si va 4 KPI stat cards', async () => {
    useAuthStore.setState({
      user: {
        id: 12,
        email: 'ds.mai@medflow.test',
        fullName: 'DS. Trần Thị Mai',
        roles: ['PHARMACY_STAFF'],
        patientCode: null,
      },
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    expect(screen.getByText('Tổng quan nhà thuốc ngoại trú')).toBeInTheDocument();
    expect(screen.getAllByText('DS. Trần Thị Mai').length).toBeGreaterThan(0);

    await waitFor(() => {
      expect(screen.getByText('Đơn chờ tiếp nhận')).toBeInTheDocument();
    });

    // 4 KPI stat cards
    expect(screen.getByText('Đơn chờ tiếp nhận')).toBeInTheDocument();
    expect(screen.getByText('Đang soạn thuốc (FEFO)')).toBeInTheDocument();
    expect(screen.getByText('Chờ gọi BN nhận thuốc')).toBeInTheDocument();
    expect(screen.getByText('Đã cấp phát hôm nay')).toBeInTheDocument();

    // Lối vào chức năng được phép của Pharmacy Staff (UC44, UC45, UC46):
    expect(screen.getAllByText('Tra cứu đơn thuốc').length).toBeGreaterThan(0);
    expect(screen.getByText('Soạn thuốc & lô FEFO')).toBeInTheDocument();
    expect(screen.getAllByText('Xác nhận cấp phát').length).toBeGreaterThan(0);

    // Không hiển thị các chức năng trái quyền (Khám lâm sàng / Cận lâm sàng / Quản trị):
    expect(screen.queryByText('Khám lâm sàng & EMR')).not.toBeInTheDocument();
    expect(screen.queryByText('Chỉ định chờ xác nhận')).not.toBeInTheDocument();
    expect(screen.queryByText('Quản lý bác sĩ')).not.toBeInTheDocument();
  });

  it('render bang hang doi don thuoc, canh bao lo FEFO, quick actions, chon don thuoc va fallback khi user null', async () => {
    useAuthStore.setState({
      user: null,
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('RX-2610-014')).toBeInTheDocument();
    });

    expect(screen.getByText('Danh sách & hàng đợi đơn thuốc ngoại trú')).toBeInTheDocument();
    expect(screen.getByText('RX-2610-014')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText('Kiểm soát lô thuốc (FEFO)')).toBeInTheDocument();
    expect(screen.getByText('3 lô')).toBeInTheDocument();
    expect(screen.getByText('Amoxicillin 500mg')).toBeInTheDocument();
    expect(screen.getAllByText('DS. Trần Thị Mai').length).toBeGreaterThan(0);

    // Chọn một đơn thuốc khác trong bảng để cập nhật khung xem nhanh chi tiết đơn
    fireEvent.click(screen.getByText('RX-2610-014'));
    expect(
      screen.getByText(/Đang chọn: RX-2610-014 • Nguyễn Văn An \(PAT-2041\)/),
    ).toBeInTheDocument();

    // Tương tác Quick Action
    fireEvent.click(screen.getByText('Chọn lô thuốc FEFO'));
    expect(screen.getByText(/Đang chọn lối vào:/)).toBeInTheDocument();

    // Thu gọn và mở rộng thanh bên
    const collapseBtn = screen.getByLabelText('Thu gọn thanh bên');
    fireEvent.click(collapseBtn);
    expect(screen.getByLabelText('Mở rộng thanh bên')).toBeInTheDocument();
  });

  it('loc danh sach don thuoc theo trang thai, tu khoa tim kiem va hien thi empty state khi khong khop', async () => {
    useAuthStore.setState({
      user: null,
      accessToken: 'tok',
      refreshToken: 'rtok',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('RX-2610-014')).toBeInTheDocument();
    });

    // Lọc theo trạng thái "Chờ phát" (READY)
    const readyFilterBtn = screen.getByRole('button', { name: 'Chờ phát' });
    fireEvent.click(readyFilterBtn);
    expect(screen.getByText('Lê Hoàng Nam')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();

    // Đổi về "Tất cả" và tìm theo mã đơn RX-2610-009
    fireEvent.click(screen.getByRole('button', { name: 'Tất cả' }));
    const searchInput = screen.getByLabelText('Tìm kiếm đơn thuốc');
    fireEvent.change(searchInput, { target: { value: 'RX-2610-009' } });

    expect(screen.getByText('Phạm Minh Tuấn')).toBeInTheDocument();
    expect(screen.queryByText('Nguyễn Văn An')).not.toBeInTheDocument();

    // Tìm kiếm từ khóa không tồn tại → hiển thị empty state
    fireEvent.change(searchInput, { target: { value: 'RX-KHONG-TON-TAI-999' } });
    expect(
      screen.getByText('Không tìm thấy đơn thuốc phù hợp với bộ lọc hiện tại.'),
    ).toBeInTheDocument();
  });

  it('xu ly trang thai loi API va cho phep thu lai (retry)', async () => {
    vi.spyOn(pharmacyApi, 'fetchPharmacyDashboardSummary')
      .mockRejectedValueOnce(new Error('Lỗi kết nối máy chủ nhà thuốc'))
      .mockResolvedValueOnce(PHARMACY_DASHBOARD_MOCK);

    useAuthStore.setState({
      user: {
        id: 9,
        email: 'pharmacy_staff01@medflow.test',
        fullName: 'DS. Trần Thị Mai',
        roles: ['PHARMACY_STAFF'],
        patientCode: null,
      },
      accessToken: 'tok-pharmacy',
      refreshToken: 'rtok-pharmacy',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('Lỗi kết nối máy chủ nhà thuốc')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /Thử lại/i }));

    await waitFor(() => {
      expect(screen.getByText('Đơn chờ tiếp nhận')).toBeInTheDocument();
    });
  });

  it('goi truc tiep GET /pharmacy/dashboard/summary khi co token va dong bo pharmacistProfile tu BE', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        ...PHARMACY_DASHBOARD_MOCK,
        facilityName: 'Bệnh viện MedFlow (giả lập)',
        facilityCode: 'HOS-DEMO-01',
        counterLabel: 'Quầy Dược 02 • Khu Ngoại trú',
        pharmacistProfile: {
          userId: 9,
          email: 'pharmacy_staff01@medflow.test',
          fullName: 'DS. Nguyễn Thu Hà',
          staffCode: 'STF-007',
          hospitalId: 1,
          hospitalCode: 'HOS-DEMO-01',
          hospitalName: 'Bệnh viện MedFlow (giả lập)',
          departmentId: 3,
          departmentName: 'Khoa Dược ngoại trú',
          roles: ['PHARMACY_STAFF'],
          permissions: ['MEDICINE_DISPENSE', 'PAYMENT_VIEW'],
        },
      },
    });

    localStorage.setItem('medflow.access_token', 'live-jwt-token');
    useAuthStore.setState({
      user: {
        id: 9,
        email: 'pharmacy_staff01@medflow.test',
        fullName: null,
        roles: ['PHARMACY_STAFF'],
        patientCode: null,
      },
      accessToken: 'live-jwt-token',
      refreshToken: 'live-refresh-token',
      isAuthenticated: true,
    });

    renderPage();

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/pharmacy/dashboard/summary', undefined);
    });

    await waitFor(() => {
      expect(screen.getAllByText('DS. Nguyễn Thu Hà').length).toBeGreaterThan(0);
    });

    expect(screen.getByText('Quầy Dược 02 • Khu Ngoại trú')).toBeInTheDocument();
  });

  it('bao ve route /pharmacy bang RequireAuth — chuyen huong ve /login?returnUrl=%2Fpharmacy khi chua login', () => {
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/pharmacy']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.queryByText('Danh sách & hàng đợi đơn thuốc ngoại trú')).not.toBeInTheDocument();
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/login?returnUrl=%2Fpharmacy');
  });
});
