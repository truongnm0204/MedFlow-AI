import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import {
  receiveMockDiagnosticPatient,
  receiveMockDiagnosticPatientsBatch,
  resetDiagnosticPatientReceiveMockState,
  resolveMockDiagnosticOrdersForReceive,
  resolveMockDiagnosticPatientReceiveDetail,
} from '../data/diagnostic-patient-receive-mock';
import { DiagnosticPatientReceivePage } from '../pages/diagnostic-patient-receive-page';
import type {
  GetDiagnosticOrdersForReceiveParams,
  ReceiveDiagnosticPatientRequest,
  ReceiveDiagnosticPatientsBatchRequest,
} from '../types/diagnostic.types';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderPage(initialEntry = '/diagnostic/orders/receive') {
  const qc = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <DiagnosticPatientReceivePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DiagnosticPatientReceivePage (UC40)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    resetDiagnosticPatientReceiveMockState();

    useAuthStore.setState({
      user: {
        id: 304,
        email: 'diagnostic@medflow.vn',
        fullName: 'CN. Lê Hoàng Minh',
        staffCode: 'DIAG-009',
        roles: ['DIAGNOSTIC_STAFF'],
        permissions: [
          'DIAGNOSTIC_RESULT_READ',
          'DIAGNOSTIC_RESULT_WRITE',
          'DIAGNOSTIC_ORDER',
          'QUEUE_VIEW',
          'QUEUE_MANAGE',
        ],
        patientCode: null,
      },
      accessToken: 'tok-diag',
      refreshToken: 'rtok-diag',
      isAuthenticated: true,
    });
  });

  it('render header UC40, sidebar Hang doi thuc hien CLS, 4 stat cards, bang tiep nhan va checklist 9 tieu chi + boi canh lam sang', async () => {
    renderPage();

    expect(
      await screen.findByText('Tiếp nhận bệnh nhân cận lâm sàng tại điểm dịch vụ'),
    ).toBeInTheDocument();
    expect(screen.getByText('Hàng đợi thực hiện CLS')).toBeInTheDocument();

    // First eligible CONFIRMED item (DIA-202610-0044) is auto-selected once list query resolves
    expect(await screen.findByTestId('receive-order-row-DIA-202610-0044')).toBeInTheDocument();

    const statCards = screen.getByTestId('diagnostic-receive-stat-cards');
    expect(statCards).toBeInTheDocument();
    expect(screen.getByTestId('stat-card-waiting-reception')).toHaveTextContent('4');
    expect(screen.getByTestId('stat-card-urgent-stat-waiting')).toHaveTextContent('3');
    expect(screen.getByTestId('stat-card-ready-in-queue')).toHaveTextContent('1');
    expect(screen.getByTestId('stat-card-in-progress-completed')).toHaveTextContent('1');

    expect(screen.getAllByText('Phạm Thu Dung').length).toBeGreaterThanOrEqual(1);
    expect(
      screen.getAllByText('Sinh hóa máu: Glucose, HbA1c, Creatinin').length,
    ).toBeGreaterThanOrEqual(1);

    // Detail panel clinical context, allergy warning & 9-item eligibility checklist
    expect(await screen.findByTestId('patient-allergy-warning')).toHaveTextContent(
      'Dị ứng thuốc cản quang chứa I-ốt mức độ nhẹ',
    );
    expect(await screen.findByText('E11.65')).toBeInTheDocument();
    expect(
      screen.getByText(
        'Đái tháo đường không phụ thuộc insuline có tăng đường huyết chưa kiểm soát',
      ),
    ).toBeInTheDocument();
    expect(screen.getByTestId('receive-eligibility-checklist')).toHaveTextContent(
      'Đạt 9/9 tiêu chí',
    );
  });

  it('loc danh sach theo tu khoa, nhom CLS, trang thai chi dinh, trang thai hang doi, muc uu tien, readyToReceiveOnly va hien thi empty state', async () => {
    renderPage();

    await screen.findByTestId('receive-order-row-DIA-202610-0044');

    const categorySelect = screen.getByLabelText('Lọc nhóm cận lâm sàng');
    fireEvent.change(categorySelect, { target: { value: 'FUNCTIONAL' } });

    await waitFor(() => {
      expect(screen.getByTestId('receive-order-row-DIA-202610-0045')).toBeInTheDocument();
      expect(screen.queryByTestId('receive-order-row-DIA-202610-0044')).not.toBeInTheDocument();
    });

    // Additional order context shown for DIA-202610-0045
    expect(await screen.findByTestId('additional-order-context')).toHaveTextContent(
      'Chỉ định bổ sung lần #2 (tiếp theo DIA-202610-0038)',
    );

    // Reset category and filter by queueStatus = READY
    fireEvent.change(categorySelect, { target: { value: 'ALL' } });
    const queueStatusSelect = screen.getByLabelText('Lọc trạng thái hàng đợi');
    fireEvent.change(queueStatusSelect, { target: { value: 'READY' } });

    await waitFor(() => {
      expect(screen.getByTestId('receive-order-row-DIA-202610-0042')).toBeInTheDocument();
      expect(screen.queryByTestId('receive-order-row-DIA-202610-0044')).not.toBeInTheDocument();
    });

    // Already received order shows audit summary
    expect(await screen.findByTestId('received-patient-audit-summary')).toHaveTextContent(
      'Bệnh nhân đã được tiếp nhận và sẵn sàng trong hàng đợi (READY)',
    );

    // Search with non-matching keyword -> empty state
    fireEvent.change(queueStatusSelect, { target: { value: 'ALL' } });
    const searchInput = screen.getByLabelText('Tìm kiếm chỉ định tiếp nhận bệnh nhân');
    fireEvent.change(searchInput, { target: { value: 'KHONG_TON_TAI_999' } });

    expect(await screen.findByTestId('diagnostic-receive-empty-state')).toHaveTextContent(
      'Không tìm thấy phiếu chỉ định cận lâm sàng phù hợp với bộ lọc tiếp nhận hiện tại.',
    );
  });

  it('kiem tra validation phia client truoc khi gui yeu cau tiep nhan (isPatientPresent, isIdentityVerified, isPreparationVerified va ky tu khong hop le)', async () => {
    const postSpy = vi.spyOn(apiClient, 'post');
    renderPage();

    await screen.findByTestId('receive-order-row-DIA-202610-0044');
    await screen.findByText('E11.65');

    const presentCheckbox = screen.getByLabelText(
      'Bệnh nhân đã có mặt tại điểm dịch vụ cận lâm sàng',
    );
    fireEvent.click(presentCheckbox);

    const submitButton = screen.getByRole('button', {
      name: /Tiếp nhận bệnh nhân & Chuyển vào hàng đợi \(READY\)/i,
    });
    fireEvent.click(submitButton);

    expect(await screen.findByTestId('receive-form-validation-error')).toHaveTextContent(
      'Nhân viên cận lâm sàng cần xác nhận bệnh nhân đã có mặt tại điểm dịch vụ (isPatientPresent = true).',
    );
    expect(postSpy).not.toHaveBeenCalled();

    // Re-check presence and uncheck identity verification
    fireEvent.click(presentCheckbox);
    const identityCheckbox = screen.getByLabelText(
      'Đã đối chiếu chính xác danh tính và thông tin hành chính bệnh nhân',
    );
    fireEvent.click(identityCheckbox);
    fireEvent.click(submitButton);

    expect(await screen.findByTestId('receive-form-validation-error')).toHaveTextContent(
      'Nhân viên cận lâm sàng cần xác nhận đã đối chiếu thông tin bệnh nhân (isIdentityVerified = true).',
    );
    expect(postSpy).not.toHaveBeenCalled();

    // Re-check identity and enter invalid '<script>' in receptionNote
    fireEvent.click(identityCheckbox);
    const noteInput = screen.getByLabelText('Ghi chú tiếp nhận bệnh nhân');
    fireEvent.change(noteInput, { target: { value: '<script>alert(1)</script>' } });
    fireEvent.click(submitButton);

    expect(await screen.findByTestId('receive-form-validation-error')).toHaveTextContent(
      'Ghi chú tiếp nhận bệnh nhân chứa ký tự không hợp lệ.',
    );
    expect(postSpy).not.toHaveBeenCalled();
  });

  it('goi GET /diagnostic/orders/receive, GET /diagnostic/orders/{code}/receive va POST /diagnostic/orders/{code}/receive khi co token', async () => {
    localStorage.setItem('medflow.access_token', 'valid-diagnostic-jwt-token');

    const getSpy = vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/orders/receive') {
        return {
          data: resolveMockDiagnosticOrdersForReceive(
            config?.params as GetDiagnosticOrdersForReceiveParams | undefined,
          ),
        };
      }
      if (typeof url === 'string' && url.endsWith('/receive')) {
        const code = decodeURIComponent(url.split('/')[3]);
        return { data: resolveMockDiagnosticPatientReceiveDetail(code) };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      if (
        typeof url === 'string' &&
        url.endsWith('/receive') &&
        url !== '/diagnostic/orders/receive'
      ) {
        const code = decodeURIComponent(url.split('/')[3]);
        return {
          data: receiveMockDiagnosticPatient(code, body as ReceiveDiagnosticPatientRequest),
        };
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    renderPage();

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/diagnostic/orders/receive', expect.anything());
    });

    await screen.findByTestId('receive-order-row-DIA-202610-0044');
    await screen.findByText('E11.65');

    const noteInput = screen.getByLabelText('Ghi chú tiếp nhận bệnh nhân');
    fireEvent.change(noteInput, {
      target: { value: 'Bệnh nhân đã có mặt tại P.XN-201, đối chiếu đúng họ tên & mã BN.' },
    });

    const submitButton = screen.getByRole('button', {
      name: /Tiếp nhận bệnh nhân & Chuyển vào hàng đợi \(READY\)/i,
    });
    fireEvent.click(submitButton);

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/diagnostic/orders/DIA-202610-0044/receive',
        expect.objectContaining({
          isPatientPresent: true,
          isIdentityVerified: true,
          isPreparationVerified: true,
          receptionNote: 'Bệnh nhân đã có mặt tại P.XN-201, đối chiếu đúng họ tên & mã BN.',
          expectedStateVersion: 2,
        }),
      );
    });

    const successBanner = await screen.findByTestId('receive-patient-success-banner');
    expect(successBanner).toHaveTextContent('DIA-202610-0044');
    expect(successBanner).toHaveTextContent('C021');
    expect(successBanner).toHaveTextContent('READY');
    expect(successBanner).toHaveTextContent('v3');
  });

  it('ho tro tiep nhan hang loat nhieu benh nhan CONFIRMED hop le qua POST /diagnostic/orders/receive', async () => {
    localStorage.setItem('medflow.access_token', 'valid-diagnostic-jwt-token');

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/orders/receive') {
        return {
          data: resolveMockDiagnosticOrdersForReceive(
            config?.params as GetDiagnosticOrdersForReceiveParams | undefined,
          ),
        };
      }
      const code = decodeURIComponent(String(url).split('/')[3]);
      return { data: resolveMockDiagnosticPatientReceiveDetail(code) };
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      if (url === '/diagnostic/orders/receive') {
        return {
          data: receiveMockDiagnosticPatientsBatch(body as ReceiveDiagnosticPatientsBatchRequest),
        };
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    renderPage();

    await screen.findByTestId('receive-order-row-DIA-202610-0044');

    const checkbox44 = screen.getByLabelText('Chọn tiếp nhận phiếu DIA-202610-0044');
    const checkbox45 = screen.getByLabelText('Chọn tiếp nhận phiếu DIA-202610-0045');
    fireEvent.click(checkbox44);
    fireEvent.click(checkbox45);

    const batchToolbar = await screen.findByTestId('batch-receive-toolbar');
    expect(batchToolbar).toHaveTextContent('Đang chọn 2 phiếu chỉ định');

    const batchButton = screen.getByRole('button', {
      name: /Tiếp nhận 2 bệnh nhân đã chọn/i,
    });
    fireEvent.click(batchButton);

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/diagnostic/orders/receive',
        expect.objectContaining({
          orderIds: [44, 45],
          isPatientPresent: true,
          isIdentityVerified: true,
          isPreparationVerified: true,
        }),
      );
    });

    expect(await screen.findByTestId('receive-patient-success-banner')).toHaveTextContent(
      'Đã tiếp nhận thành công 2 phiếu chỉ định vào hàng đợi kỹ thuật',
    );
  });

  it('vo hieu hoa tiep nhan va hien thi ly do khi phieu khong du dieu kien (phong bao tri / chua xac nhan UC39) hoac khi nhan vien thieu quyen QUEUE_MANAGE', async () => {
    renderPage();

    const ineligibleRow = await screen.findByTestId('receive-order-row-DIA-202610-0048');
    fireEvent.click(ineligibleRow);

    const disabledReason = await screen.findByTestId('order-receive-disabled-reason');
    expect(disabledReason).toHaveTextContent(
      "Phòng thực hiện cận lâm sàng 'P.MRI-207' cho dịch vụ 'Chụp cộng hưởng từ (MRI) cột sống thắt lưng' hiện đang bảo trì hoặc ngừng hoạt động.",
    );

    const submitButton = screen.getByRole('button', {
      name: /Tiếp nhận bệnh nhân & Chuyển vào hàng đợi \(READY\)/i,
    });
    expect(submitButton).toBeDisabled();

    // Unconfirmed ORDERED order shows UC39 prerequisite reason
    const unconfirmedRow = screen.getByTestId('receive-order-row-DIA-202610-0047');
    fireEvent.click(unconfirmedRow);
    expect(await screen.findByTestId('order-receive-disabled-reason')).toHaveTextContent(
      "Phiếu chỉ định cận lâm sàng 'DIA-202610-0047' chưa được xác nhận điều kiện xử lý (ORDERED). Vui lòng xác nhận phiếu chỉ định (UC39) trước khi tiếp nhận bệnh nhân.",
    );

    // Staff lacking QUEUE_MANAGE permission
    useAuthStore.setState({
      user: {
        id: 305,
        email: 'viewer@medflow.vn',
        fullName: 'KTV. Chỉ Xem',
        roles: ['DIAGNOSTIC_STAFF'],
        permissions: ['DIAGNOSTIC_RESULT_READ'],
        patientCode: null,
      },
      accessToken: 'tok-diag-view',
      refreshToken: 'rtok-diag-view',
      isAuthenticated: true,
    });

    const eligibleRow = screen.getByTestId('receive-order-row-DIA-202610-0044');
    fireEvent.click(eligibleRow);

    expect(await screen.findByTestId('missing-receive-permission-alert')).toHaveTextContent(
      'Tài khoản chưa được cấp quyền tiếp nhận bệnh nhân vào hàng đợi CLS',
    );
    expect(
      screen.getByRole('button', {
        name: /Tiếp nhận bệnh nhân & Chuyển vào hàng đợi \(READY\)/i,
      }),
    ).toBeDisabled();
  });

  it('hien thi thong bao loi nghiep vu 409 Conflict tu BE khi tiep nhan that bai', async () => {
    localStorage.setItem('medflow.access_token', 'valid-diagnostic-jwt-token');

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/orders/receive') {
        return {
          data: resolveMockDiagnosticOrdersForReceive(
            config?.params as GetDiagnosticOrdersForReceiveParams | undefined,
          ),
        };
      }
      const code = decodeURIComponent(String(url).split('/')[3]);
      return { data: resolveMockDiagnosticPatientReceiveDetail(code) };
    });

    const conflictError = new AxiosError('Conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 409,
      statusText: 'Conflict',
      headers: {},
      config: {} as never,
      data: {
        detail:
          'Dữ liệu hàng đợi cận lâm sàng đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại.',
      },
    });

    vi.spyOn(apiClient, 'post').mockRejectedValue(conflictError);

    renderPage();

    await screen.findByTestId('receive-order-row-DIA-202610-0044');
    await screen.findByText('E11.65');

    const submitButton = screen.getByRole('button', {
      name: /Tiếp nhận bệnh nhân & Chuyển vào hàng đợi \(READY\)/i,
    });
    fireEvent.click(submitButton);

    expect(await screen.findByTestId('receive-action-error-banner')).toHaveTextContent(
      'Dữ liệu hàng đợi cận lâm sàng đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại.',
    );
  });

  it('bao ve route /diagnostic/orders/receive bang RequireAuth va RequireDiagnostic', () => {
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/diagnostic/orders/receive']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByTestId('location-probe')).toHaveTextContent(
      '/login?returnUrl=%2Fdiagnostic%2Forders%2Freceive',
    );
    unmount();

    // Authenticated as PATIENT -> redirected to /
    useAuthStore.setState({
      user: {
        id: 10,
        email: 'patient@medflow.vn',
        fullName: 'Bệnh Nhân A',
        roles: ['PATIENT'],
        patientCode: 'PAT-001',
      },
      accessToken: 'tok-pat',
      refreshToken: 'rtok-pat',
      isAuthenticated: true,
    });

    const { unmount: unmountPatient } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/diagnostic/orders/receive']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/');
    unmountPatient();

    // Authenticated as DOCTOR -> redirected to /doctor
    useAuthStore.setState({
      user: {
        id: 20,
        email: 'doctor@medflow.vn',
        fullName: 'BS. Nguyễn Văn B',
        roles: ['DOCTOR'],
        patientCode: null,
      },
      accessToken: 'tok-doc',
      refreshToken: 'rtok-doc',
      isAuthenticated: true,
    });

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/diagnostic/orders/receive']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/doctor');
  });
});
