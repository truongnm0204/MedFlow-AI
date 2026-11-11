import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import {
  confirmMockDiagnosticOrder,
  confirmMockDiagnosticOrdersBatch,
  resetDiagnosticOrderReviewMockState,
  resolveMockDiagnosticOrderReviewDetail,
  resolveMockDiagnosticOrdersForReview,
} from '../data/diagnostic-order-review-mock';
import { DiagnosticOrderReviewPage } from '../pages/diagnostic-order-review-page';
import type { GetDiagnosticOrdersForReviewParams } from '../types/diagnostic.types';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderPage(initialEntry = '/diagnostic/orders') {
  const qc = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <DiagnosticOrderReviewPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DiagnosticOrderReviewPage (UC39)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    resetDiagnosticOrderReviewMockState();

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

  it('render header UC39, sidebar Chi dinh cho xac nhan, 4 stat cards, bang chi dinh va checklist 8 tieu chi + boi canh lam sang', async () => {
    renderPage();

    expect(
      await screen.findByText('Kiểm tra & Xác nhận chỉ định cận lâm sàng'),
    ).toBeInTheDocument();
    expect(screen.getByText('Chỉ định chờ xác nhận')).toBeInTheDocument();

    // First ORDERED item (DIA-202610-0044) is auto-selected once list query resolves
    expect(await screen.findByTestId('review-order-row-DIA-202610-0044')).toBeInTheDocument();

    const statCards = screen.getByTestId('diagnostic-review-stat-cards');
    expect(statCards).toBeInTheDocument();
    expect(screen.getByTestId('stat-card-pending-confirmation')).toHaveTextContent('4');
    expect(screen.getByTestId('stat-card-urgent-stat-pending')).toHaveTextContent('2');
    expect(screen.getByTestId('stat-card-confirmed-orders')).toHaveTextContent('1');
    expect(screen.getByTestId('stat-card-in-progress-completed')).toHaveTextContent('2');

    expect(screen.getAllByText('Phạm Thu Dung').length).toBeGreaterThanOrEqual(1);
    expect(
      screen.getAllByText('Sinh hóa máu: Glucose, HbA1c, Creatinin').length,
    ).toBeGreaterThanOrEqual(1);

    // Detail panel clinical context & allergy warning & 8-item eligibility checklist
    expect(await screen.findByTestId('patient-allergy-warning')).toHaveTextContent(
      'Dị ứng thuốc cản quang chứa I-ốt mức độ nhẹ',
    );
    expect(await screen.findByText('E11.65')).toBeInTheDocument();
    expect(
      screen.getByText(
        'Đái tháo đường không phụ thuộc insuline có tăng đường huyết chưa kiểm soát',
      ),
    ).toBeInTheDocument();
    expect(screen.getByTestId('eligibility-checklist')).toHaveTextContent('Đạt 8/8 tiêu chí');
  });

  it('loc danh sach chi dinh theo tu khoa, nhom CLS, trang thai, muc uu tien, pendingOnly va hien thi empty state khi khong khop', async () => {
    renderPage();

    await screen.findByTestId('review-order-row-DIA-202610-0044');

    const categorySelect = screen.getByLabelText('Lọc nhóm cận lâm sàng');
    fireEvent.change(categorySelect, { target: { value: 'FUNCTIONAL' } });

    await waitFor(() => {
      expect(screen.getByTestId('review-order-row-DIA-202610-0045')).toBeInTheDocument();
      expect(screen.queryByTestId('review-order-row-DIA-202610-0044')).not.toBeInTheDocument();
    });

    // Additional order context shown for DIA-202610-0045
    expect(await screen.findByTestId('additional-order-context')).toHaveTextContent(
      'Chỉ định bổ sung lần #2 (tiếp theo DIA-202610-0038)',
    );

    // Reset category and test search with non-matching keyword
    fireEvent.change(categorySelect, { target: { value: 'ALL' } });
    const searchInput = screen.getByLabelText('Tìm kiếm chỉ định chờ xác nhận');
    fireEvent.change(searchInput, { target: { value: 'KHONG_TON_TAI_999' } });

    expect(await screen.findByTestId('diagnostic-review-empty-state')).toHaveTextContent(
      'Không tìm thấy phiếu chỉ định cận lâm sàng phù hợp với bộ lọc hiện tại.',
    );
  });

  it('kiem tra validation phia client truoc khi gui yeu cau xac nhan (isEligibilityVerified va ky tu khong hop le)', async () => {
    const postSpy = vi.spyOn(apiClient, 'post');
    renderPage();

    await screen.findByTestId('review-order-row-DIA-202610-0044');
    await screen.findByText('E11.65');

    const eligibilityCheckbox = screen.getByLabelText(
      'Tôi đã kiểm tra đối chiếu thông tin bệnh nhân và xác nhận phiếu chỉ định đủ điều kiện',
    );
    fireEvent.click(eligibilityCheckbox);

    const submitButton = screen.getByRole('button', {
      name: /Xác nhận chỉ định & Xếp hàng đợi/i,
    });
    fireEvent.click(submitButton);

    expect(await screen.findByTestId('confirm-form-validation-error')).toHaveTextContent(
      'Nhân viên cận lâm sàng cần xác nhận phiếu chỉ định đủ điều kiện xử lý (isEligibilityVerified = true).',
    );
    expect(postSpy).not.toHaveBeenCalled();

    // Re-check eligibility and enter invalid '<script>' in confirmationNote
    fireEvent.click(eligibilityCheckbox);
    const noteInput = screen.getByLabelText('Ghi chú xác nhận chỉ định');
    fireEvent.change(noteInput, { target: { value: '<script>alert(1)</script>' } });
    fireEvent.click(submitButton);

    expect(await screen.findByTestId('confirm-form-validation-error')).toHaveTextContent(
      'Ghi chú xác nhận chỉ định chứa ký tự không hợp lệ.',
    );
    expect(postSpy).not.toHaveBeenCalled();
  });

  it('goi GET /diagnostic/orders/review, GET /diagnostic/orders/{code}/review va POST /diagnostic/orders/{code}/confirm khi co token', async () => {
    localStorage.setItem('medflow.access_token', 'valid-diagnostic-jwt-token');

    const getSpy = vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/orders/review') {
        return {
          data: resolveMockDiagnosticOrdersForReview(
            config?.params as GetDiagnosticOrdersForReviewParams | undefined,
          ),
        };
      }
      if (typeof url === 'string' && url.endsWith('/review')) {
        const code = decodeURIComponent(url.split('/')[3]);
        return { data: resolveMockDiagnosticOrderReviewDetail(code) };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      if (
        typeof url === 'string' &&
        url.endsWith('/confirm') &&
        url !== '/diagnostic/orders/confirm'
      ) {
        const code = decodeURIComponent(url.split('/')[3]);
        return { data: confirmMockDiagnosticOrder(code, body as Record<string, unknown>) };
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    renderPage();

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/diagnostic/orders/review', expect.anything());
    });

    await screen.findByTestId('review-order-row-DIA-202610-0044');
    await screen.findByText('E11.65');

    const noteInput = screen.getByLabelText('Ghi chú xác nhận chỉ định');
    fireEvent.change(noteInput, {
      target: { value: 'Đã kiểm tra thông tin hành chính và lấy mẫu tại P.XN-201.' },
    });

    const submitButton = screen.getByRole('button', {
      name: /Xác nhận chỉ định & Xếp hàng đợi/i,
    });
    fireEvent.click(submitButton);

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/diagnostic/orders/DIA-202610-0044/confirm',
        expect.objectContaining({
          isEligibilityVerified: true,
          confirmationNote: 'Đã kiểm tra thông tin hành chính và lấy mẫu tại P.XN-201.',
          expectedStateVersion: 1,
        }),
      );
    });

    const successBanner = await screen.findByTestId('confirm-order-success-banner');
    expect(successBanner).toHaveTextContent('DIA-202610-0044');
    expect(successBanner).toHaveTextContent('C021');
    expect(successBanner).toHaveTextContent('v2');
  });

  it('ho tro xac nhan hang loat nhieu phieu chi dinh ORDERED hop le qua POST /diagnostic/orders/confirm', async () => {
    localStorage.setItem('medflow.access_token', 'valid-diagnostic-jwt-token');

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/orders/review') {
        return {
          data: resolveMockDiagnosticOrdersForReview(
            config?.params as GetDiagnosticOrdersForReviewParams | undefined,
          ),
        };
      }
      const code = decodeURIComponent(String(url).split('/')[3]);
      return { data: resolveMockDiagnosticOrderReviewDetail(code) };
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      if (url === '/diagnostic/orders/confirm') {
        return {
          data: confirmMockDiagnosticOrdersBatch(body as Record<string, unknown>),
        };
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    renderPage();

    await screen.findByTestId('review-order-row-DIA-202610-0044');

    const checkbox44 = screen.getByLabelText('Chọn phiếu DIA-202610-0044');
    const checkbox45 = screen.getByLabelText('Chọn phiếu DIA-202610-0045');
    fireEvent.click(checkbox44);
    fireEvent.click(checkbox45);

    const batchToolbar = await screen.findByTestId('batch-confirm-toolbar');
    expect(batchToolbar).toHaveTextContent('Đang chọn 2 phiếu chỉ định');

    const batchButton = screen.getByRole('button', {
      name: /Xác nhận 2 phiếu đã chọn/i,
    });
    fireEvent.click(batchButton);

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/diagnostic/orders/confirm',
        expect.objectContaining({
          orderIds: [44, 45],
          isEligibilityVerified: true,
        }),
      );
    });

    expect(await screen.findByTestId('confirm-order-success-banner')).toHaveTextContent(
      'Đã xác nhận thành công 2 phiếu chỉ định',
    );
  });

  it('vo hieu hoa xac nhan va hien thi ly do khi phieu khong du dieu kien (phong tam ngung) hoac khi nhan vien thieu quyen', async () => {
    renderPage();

    const ineligibleRow = await screen.findByTestId('review-order-row-DIA-202610-0048');
    fireEvent.click(ineligibleRow);

    const disabledReason = await screen.findByTestId('order-disabled-reason');
    expect(disabledReason).toHaveTextContent(
      "Phòng thực hiện cận lâm sàng 'P.MRI-207' cho dịch vụ 'Chụp cộng hưởng từ (MRI) cột sống thắt lưng' hiện đang bảo trì hoặc ngừng hoạt động.",
    );

    const submitButton = screen.getByRole('button', {
      name: /Xác nhận chỉ định & Xếp hàng đợi/i,
    });
    expect(submitButton).toBeDisabled();

    // Now test when staff lacks confirmation permissions
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

    const eligibleRow = screen.getByTestId('review-order-row-DIA-202610-0044');
    fireEvent.click(eligibleRow);

    expect(await screen.findByTestId('missing-confirm-permission-alert')).toHaveTextContent(
      'Tài khoản chưa được cấp quyền xác nhận chỉ định CLS',
    );
    expect(
      screen.getByRole('button', { name: /Xác nhận chỉ định & Xếp hàng đợi/i }),
    ).toBeDisabled();
  });

  it('hien thi thong bao loi nghiep vu 409 Conflict tu BE khi xac nhan that bai', async () => {
    localStorage.setItem('medflow.access_token', 'valid-diagnostic-jwt-token');

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/orders/review') {
        return {
          data: resolveMockDiagnosticOrdersForReview(
            config?.params as GetDiagnosticOrdersForReviewParams | undefined,
          ),
        };
      }
      const code = decodeURIComponent(String(url).split('/')[3]);
      return { data: resolveMockDiagnosticOrderReviewDetail(code) };
    });

    const conflictError = new AxiosError('Conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 409,
      statusText: 'Conflict',
      headers: {},
      config: {} as never,
      data: {
        detail:
          'Phiên bản trạng thái hàng đợi đã thay đổi (ExpectedStateVersion = 1, hiện tại = 2). Vui lòng tải lại trước khi xác nhận.',
      },
    });

    vi.spyOn(apiClient, 'post').mockRejectedValue(conflictError);

    renderPage();

    await screen.findByTestId('review-order-row-DIA-202610-0044');
    await screen.findByText('E11.65');

    const submitButton = screen.getByRole('button', {
      name: /Xác nhận chỉ định & Xếp hàng đợi/i,
    });
    fireEvent.click(submitButton);

    expect(await screen.findByTestId('confirm-action-error-banner')).toHaveTextContent(
      'Phiên bản trạng thái hàng đợi đã thay đổi (ExpectedStateVersion = 1, hiện tại = 2). Vui lòng tải lại trước khi xác nhận.',
    );
  });

  it('bao ve route /diagnostic/orders bang RequireAuth va RequireDiagnostic', () => {
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/diagnostic/orders']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByTestId('location-probe')).toHaveTextContent(
      '/login?returnUrl=%2Fdiagnostic%2Forders',
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
        <MemoryRouter initialEntries={['/diagnostic/orders']}>
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
        <MemoryRouter initialEntries={['/diagnostic/orders']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId('location-probe')).toHaveTextContent('/doctor');
  });
});
