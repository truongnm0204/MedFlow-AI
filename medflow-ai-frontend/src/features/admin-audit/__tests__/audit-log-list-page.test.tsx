import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';

import * as auditLogApi from '../api/audit-log.api';
import { AuditLogListPage } from '../pages/audit-log-list-page';
import type { AuditLogDetailDto, AuditLogListResponseDto } from '../types/audit-log.types';

const MOCK_AUDIT_LIST: AuditLogListResponseDto = {
  data: [
    {
      id: 101,
      auditCode: 'AUD-0101',
      actorUserId: 13,
      actorEmail: 'system_admin01@medflow.test',
      actorFullName: 'QTHT Lưu Huy',
      hospitalId: 1,
      hospitalName: 'Bệnh viện MedFlow Cơ sở 1',
      action: 'INTEGRATION_CONFIG_UPDATED',
      category: 'CONFIG',
      entityType: 'INTEGRATION_CONFIG',
      entityId: '1:VIETQR_SEPAY',
      reason: 'Cập nhật tài khoản nhận viện phí VietQR',
      ipAddress: '10.20.1.15',
      occurredAt: '2026-11-24T17:30:00Z',
      occurredAtHospitalLocal: '2026-11-25T00:30:00',
      hasMetadata: true,
      changeData: '{"ProviderKey":"VIETQR_SEPAY","webhookApiKey":"***REDACTED***"}',
    },
    {
      id: 102,
      auditCode: 'AUD-0102',
      actorUserId: 11,
      actorEmail: 'hospital_admin01@medflow.test',
      actorFullName: 'QLBV Phan Bình',
      hospitalId: 1,
      hospitalName: 'Bệnh viện MedFlow Cơ sở 1',
      action: 'LOGIN_SUCCESS',
      category: 'AUTH',
      entityType: 'USER',
      entityId: '11',
      reason: 'Đăng nhập thành công',
      ipAddress: '10.20.1.40',
      occurredAt: '2026-11-24T15:10:00Z',
      occurredAtHospitalLocal: '2026-11-24T22:10:00',
      hasMetadata: false,
      changeData: null,
    },
  ],
  items: [
    {
      id: 101,
      auditCode: 'AUD-0101',
      actorUserId: 13,
      actorEmail: 'system_admin01@medflow.test',
      actorFullName: 'QTHT Lưu Huy',
      hospitalId: 1,
      hospitalName: 'Bệnh viện MedFlow Cơ sở 1',
      action: 'INTEGRATION_CONFIG_UPDATED',
      category: 'CONFIG',
      entityType: 'INTEGRATION_CONFIG',
      entityId: '1:VIETQR_SEPAY',
      reason: 'Cập nhật tài khoản nhận viện phí VietQR',
      ipAddress: '10.20.1.15',
      occurredAt: '2026-11-24T17:30:00Z',
      occurredAtHospitalLocal: '2026-11-25T00:30:00',
      hasMetadata: true,
      changeData: '{"ProviderKey":"VIETQR_SEPAY","webhookApiKey":"***REDACTED***"}',
    },
    {
      id: 102,
      auditCode: 'AUD-0102',
      actorUserId: 11,
      actorEmail: 'hospital_admin01@medflow.test',
      actorFullName: 'QLBV Phan Bình',
      hospitalId: 1,
      hospitalName: 'Bệnh viện MedFlow Cơ sở 1',
      action: 'LOGIN_SUCCESS',
      category: 'AUTH',
      entityType: 'USER',
      entityId: '11',
      reason: 'Đăng nhập thành công',
      ipAddress: '10.20.1.40',
      occurredAt: '2026-11-24T15:10:00Z',
      occurredAtHospitalLocal: '2026-11-24T22:10:00',
      hasMetadata: false,
      changeData: null,
    },
  ],
  total: 25,
  page: 1,
  pageSize: 20,
  totalPages: 2,
  metrics: {
    totalLogs: 25,
    last24HoursCount: 8,
    authEventsCount: 10,
    configAndRbacEventsCount: 15,
  },
  availableActions: ['INTEGRATION_CONFIG_UPDATED', 'LOGIN_SUCCESS'],
  availableEntityTypes: ['INTEGRATION_CONFIG', 'USER'],
  availableHospitals: [
    { id: 1, code: 'HOS-01', name: 'Bệnh viện MedFlow Cơ sở 1' },
    { id: 2, code: 'HOS-02', name: 'Bệnh viện MedFlow Cơ sở 2' },
  ],
};

const MOCK_DETAIL_101: AuditLogDetailDto = {
  id: 101,
  auditCode: 'AUD-0101',
  actorUserId: 13,
  actorEmail: 'system_admin01@medflow.test',
  actorFullName: 'QTHT Lưu Huy',
  hospitalId: 1,
  hospitalName: 'Bệnh viện MedFlow Cơ sở 1',
  action: 'INTEGRATION_CONFIG_UPDATED',
  category: 'CONFIG',
  entityType: 'INTEGRATION_CONFIG',
  entityId: '1:VIETQR_SEPAY',
  reason: 'Cập nhật tài khoản nhận viện phí VietQR',
  ipAddress: '10.20.1.15',
  occurredAt: '2026-11-24T17:30:00Z',
  occurredAtHospitalLocal: '2026-11-25T00:30:00',
  changeData: '{"ProviderKey":"VIETQR_SEPAY","webhookApiKey":"***REDACTED***","enabled":true}',
  redactedFields: ['webhookApiKey'],
};

function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="location-probe">{loc.pathname + loc.search}</div>;
}

function renderAuditPage() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/admin/audit-logs']}>
        <AuditLogListPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AuditLogListPage (UC60 — #245, #247)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    vi.spyOn(auditLogApi, 'fetchAuditLogs').mockResolvedValue(MOCK_AUDIT_LIST);
    vi.spyOn(auditLogApi, 'fetchAuditLogDetail').mockResolvedValue(MOCK_DETAIL_101);

    useAuthStore.setState({
      user: {
        id: 13,
        email: 'system_admin01@medflow.test',
        fullName: 'QTHT Lưu Huy',
        roles: ['SYSTEM_ADMIN'],
        patientCode: null,
        staffCode: 'STF-011',
      },
      accessToken: 'tok-sysadmin',
      refreshToken: 'rtok-sysadmin',
      isAuthenticated: true,
    });
  });

  it('hien thi thong ke, bang nhat ky UTC+7 va mo ngan keo xem chi tiet JSON da che truong nhay cam', async () => {
    renderAuditPage();

    expect(
      screen.getByRole('heading', { name: 'Nhật ký Kiểm toán Hệ thống (Audit Log)' }),
    ).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('AUD-0101')).toBeInTheDocument();
    });

    expect(screen.getByText('25/11/2026 00:30:00 (UTC+7)')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Nhật ký kiểm toán' })).toHaveAttribute(
      'aria-current',
      'page',
    );

    const detailButtons = screen.getAllByRole('button', { name: /Chi tiết JSON/i });
    fireEvent.click(detailButtons[0]);

    await waitFor(() => {
      expect(auditLogApi.fetchAuditLogDetail).toHaveBeenCalledWith(101);
    });

    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(await screen.findByText(/Bảo vệ dữ liệu nhạy cảm/i)).toBeInTheDocument();
    expect(screen.getByTestId('audit-metadata-json')).toHaveTextContent('***REDACTED***');

    fireEvent.click(screen.getByRole('button', { name: 'Đóng ngăn chi tiết nhật ký' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('kiem tra hop le khoang ngay loc, goi API voi tham so loc va phan trang', async () => {
    const fetchSpy = vi.spyOn(auditLogApi, 'fetchAuditLogs').mockResolvedValue(MOCK_AUDIT_LIST);

    renderAuditPage();

    await waitFor(() => {
      expect(fetchSpy).toHaveBeenCalledWith({ page: 1, pageSize: 20 });
    });

    const fromInput = screen.getByLabelText(/Từ ngày \(UTC\+7\)/i);
    const toInput = screen.getByLabelText(/Đến ngày \(UTC\+7\)/i);
    const submitBtn = screen.getByRole('button', { name: /Lọc nhật ký/i });

    // Invalid: fromDate > toDate
    fireEvent.change(fromInput, { target: { value: '2026-11-26' } });
    fireEvent.change(toInput, { target: { value: '2026-11-20' } });
    fireEvent.click(submitBtn);

    expect(
      screen.getByText('Ngày bắt đầu tra cứu không được lớn hơn ngày kết thúc.'),
    ).toBeInTheDocument();
    expect(fetchSpy).toHaveBeenCalledTimes(1);

    // Valid filter submission
    fireEvent.change(screen.getByLabelText('Từ khóa tìm kiếm'), {
      target: { value: 'VietQR' },
    });
    fireEvent.change(screen.getByLabelText('Hành động (Action)'), {
      target: { value: 'INTEGRATION_CONFIG_UPDATED' },
    });
    fireEvent.change(fromInput, { target: { value: '2026-11-24' } });
    fireEvent.change(toInput, { target: { value: '2026-11-25' } });
    fireEvent.change(screen.getByLabelText('Phạm vi cơ sở y tế'), {
      target: { value: '1' },
    });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(fetchSpy).toHaveBeenLastCalledWith({
        keyword: 'VietQR',
        action: 'INTEGRATION_CONFIG_UPDATED',
        entityType: undefined,
        actorUserId: null,
        hospitalId: 1,
        fromDate: '2026-11-24',
        toDate: '2026-11-25',
        page: 1,
        pageSize: 20,
      });
    });

    // Next page pagination
    fireEvent.click(screen.getByRole('button', { name: /Trang tiếp/i }));
    await waitFor(() => {
      expect(fetchSpy).toHaveBeenLastCalledWith(
        expect.objectContaining({
          page: 2,
          pageSize: 20,
        }),
      );
    });
  });

  it('xu ly trang thai loi API kem nut Thu lai va bao ve route /admin/audit-logs + /system-admin/audit-logs', async () => {
    vi.spyOn(auditLogApi, 'fetchAuditLogs')
      .mockRejectedValueOnce(new Error('Tài khoản chưa được cấp quyền AUDIT_READ'))
      .mockResolvedValueOnce(MOCK_AUDIT_LIST);

    const { unmount } = renderAuditPage();

    await waitFor(() => {
      expect(screen.getByText('Tài khoản chưa được cấp quyền AUDIT_READ')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
    await waitFor(() => {
      expect(screen.getByText('AUD-0101')).toBeInTheDocument();
    });
    unmount();

    // Route guard: HOSPITAL_ADMIN redirected from /system-admin/audit-logs to /hospital-admin
    useAuthStore.setState({
      user: {
        id: 11,
        email: 'hospital_admin01@medflow.test',
        fullName: 'QLBV Phan Bình',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
      },
      accessToken: 'tok-hadmin',
      refreshToken: 'rtok-hadmin',
      isAuthenticated: true,
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/system-admin/audit-logs']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByTestId('location-probe')).toHaveTextContent('/hospital-admin');
  });
});
