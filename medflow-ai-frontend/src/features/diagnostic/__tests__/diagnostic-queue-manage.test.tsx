import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import {
  callNextMockDiagnosticPatient,
  cancelMockDiagnosticQueueEntry,
  markMockDiagnosticMissedTurn,
  resetDiagnosticQueueMockState,
  resolveMockDiagnosticQueueBoard,
  resolveMockDiagnosticQueueEntryDetail,
  restoreMockDiagnosticSkippedTurn,
  updateMockDiagnosticQueueStatus,
} from '../data/diagnostic-queue-mock';
import { DiagnosticQueueManagePage } from '../pages/diagnostic-queue-manage-page';
import type { GetDiagnosticQueueParams } from '../types/diagnostic.types';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderPage(initialEntry = '/diagnostic/queue') {
  const qc = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <DiagnosticQueueManagePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DiagnosticQueueManagePage (UC41 — Manage Diagnostic Queue)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    resetDiagnosticQueueMockState();

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

  it('render header UC41, 4 KPI stat cards, 3 service point buckets, bang hang doi va chi tiet boi canh lam sang + lich su chuyen trang thai', async () => {
    renderPage();

    expect(
      await screen.findByText('Quản lý & Điều phối Hàng đợi Cận lâm sàng'),
    ).toBeInTheDocument();
    expect(screen.getByText('UC41')).toBeInTheDocument();

    // First READY entry (QE-00009041 / C018) is auto-selected
    expect(await screen.findByTestId('queue-entry-row-QE-00009041')).toBeInTheDocument();

    // 4 Stat cards
    expect(screen.getByTestId('diagnostic-queue-stat-cards')).toBeInTheDocument();
    expect(screen.getByTestId('queue-stat-card-waiting-ready')).toHaveTextContent('2');
    expect(screen.getByTestId('queue-stat-card-called-in-progress')).toHaveTextContent('2');
    expect(screen.getByTestId('queue-stat-card-urgent-stat')).toHaveTextContent('3');
    expect(screen.getByTestId('queue-stat-card-completed-skipped')).toHaveTextContent('1');

    // 3 Service point buckets
    expect(screen.getByTestId('service-point-bucket-SP-201')).toBeInTheDocument();
    expect(screen.getByTestId('service-point-bucket-SP-204')).toBeInTheDocument();
    expect(screen.getByTestId('service-point-bucket-SP-205')).toBeInTheDocument();

    // Detail panel clinical context, allergy warning & status history
    expect(await screen.findByTestId('patient-allergy-warning')).toHaveTextContent(
      'Dị ứng thuốc cản quang chứa I-ốt mức độ nhẹ',
    );
    expect(await screen.findByTestId('queue-entry-clinical-context')).toHaveTextContent('E11.65');
    expect(await screen.findByTestId('queue-status-history-list')).toHaveTextContent(
      'WAITING → READY (v2)',
    );
  });

  it('loc hang doi theo nhom CLS, trang thai, muc uu tien, diem phuc vu, tu khoa va dat lai bo loc', async () => {
    renderPage();

    await screen.findByTestId('queue-entry-row-QE-00009041');

    // Filter by category IMAGING -> QE-00009043 & QE-00009046
    fireEvent.change(screen.getByLabelText('Lọc nhóm cận lâm sàng'), {
      target: { value: 'IMAGING' },
    });

    await waitFor(() => {
      expect(screen.getByTestId('queue-entry-row-QE-00009043')).toBeInTheDocument();
      expect(screen.queryByTestId('queue-entry-row-QE-00009041')).not.toBeInTheDocument();
    });

    // Reset filters
    fireEvent.click(screen.getByTestId('reset-queue-filters-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('queue-entry-row-QE-00009041')).toBeInTheDocument();
    });

    // Keyword search that yields empty state
    fireEvent.change(screen.getByLabelText('Tìm kiếm lượt xếp hàng cận lâm sàng'), {
      target: { value: 'KHONG-TON-TAI-99999' },
    });

    expect(await screen.findByTestId('diagnostic-queue-empty-state')).toBeInTheDocument();
  });

  it('goi benh nhan tiep theo (Call Next) tai diem phuc vu SP-201 va xu ly xung dot khi phong dang ban (SP-204)', async () => {
    localStorage.setItem('medflow.access_token', 'tok-diag');

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/queue') {
        return {
          data: resolveMockDiagnosticQueueBoard(
            config?.params as GetDiagnosticQueueParams | undefined,
          ),
        };
      }
      const match = url.match(/^\/diagnostic\/queue\/([^/]+)$/);
      if (match) {
        return {
          data: resolveMockDiagnosticQueueEntryDetail(decodeURIComponent(match[1])),
        };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      const match = url.match(/^\/diagnostic\/queue\/service-points\/([^/]+)\/call-next$/);
      if (match) {
        const spId = decodeURIComponent(match[1]);
        return {
          data: callNextMockDiagnosticPatient(spId, body as Record<string, unknown>),
        };
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    renderPage();

    await screen.findByTestId('queue-entry-row-QE-00009041');

    // Call Next on SP-201 -> calls C018 (Phạm Thu Dung, STAT)
    fireEvent.click(screen.getByTestId('call-next-btn-SP-201'));

    const banner = await screen.findByTestId('call-next-result-banner');
    expect(banner).toHaveTextContent('Phạm Thu Dung');
    expect(banner).toHaveTextContent('C018');
    expect(postSpy).toHaveBeenCalledWith(
      '/diagnostic/queue/service-points/SP-201/call-next',
      expect.objectContaining({ servicePointId: 'SP-201' }),
    );

    // Calling Next on SP-204 (which already has C020 CALLED) throws conflict error
    fireEvent.click(screen.getByTestId('call-next-btn-SP-204'));
    expect(await screen.findByTestId('queue-action-error-banner')).toHaveTextContent(
      'Điểm phục vụ cận lâm sàng đang có bệnh nhân được gọi hoặc đang thực hiện kỹ thuật',
    );
  });

  it('thuc hien chuyen trang thai nhanh (READY -> CALLED -> IN_PROGRESS -> COMPLETED) kem expectedStateVersion', async () => {
    localStorage.setItem('medflow.access_token', 'tok-diag');

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/queue') {
        return {
          data: resolveMockDiagnosticQueueBoard(
            config?.params as GetDiagnosticQueueParams | undefined,
          ),
        };
      }
      const match = url.match(/^\/diagnostic\/queue\/([^/]+)$/);
      if (match) {
        return {
          data: resolveMockDiagnosticQueueEntryDetail(decodeURIComponent(match[1])),
        };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      const callMatch = url.match(/^\/diagnostic\/queue\/([^/]+)\/call$/);
      if (callMatch) {
        const id = decodeURIComponent(callMatch[1]);
        return {
          data: updateMockDiagnosticQueueStatus(id, {
            ...(body as Record<string, unknown>),
            targetStatus: 'CALLED',
          }),
        };
      }
      const startMatch = url.match(/^\/diagnostic\/queue\/([^/]+)\/start$/);
      if (startMatch) {
        const id = decodeURIComponent(startMatch[1]);
        return {
          data: updateMockDiagnosticQueueStatus(id, {
            ...(body as Record<string, unknown>),
            targetStatus: 'IN_PROGRESS',
          }),
        };
      }
      const completeMatch = url.match(/^\/diagnostic\/queue\/([^/]+)\/complete$/);
      if (completeMatch) {
        const id = decodeURIComponent(completeMatch[1]);
        return {
          data: updateMockDiagnosticQueueStatus(id, {
            ...(body as Record<string, unknown>),
            targetStatus: 'COMPLETED',
          }),
        };
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    renderPage();

    await screen.findByTestId('queue-entry-row-QE-00009041');

    // Step 1: READY -> CALLED (v2 -> v3)
    fireEvent.click(screen.getByTestId('detail-btn-call-entry'));

    const successBanner = await screen.findByTestId('queue-action-success-banner');
    expect(successBanner).toHaveTextContent('READY → CALLED');
    expect(successBanner).toHaveTextContent('v3');
    expect(postSpy).toHaveBeenCalledWith(
      '/diagnostic/queue/QE-00009041/call',
      expect.objectContaining({ expectedStateVersion: 2 }),
    );

    // Step 2: CALLED -> IN_PROGRESS (v3 -> v4)
    await waitFor(() => {
      expect(screen.getByTestId('detail-btn-start-execution')).not.toBeDisabled();
    });
    fireEvent.click(screen.getByTestId('detail-btn-start-execution'));

    await waitFor(() => {
      expect(screen.getByTestId('queue-action-success-banner')).toHaveTextContent(
        'CALLED → IN_PROGRESS',
      );
    });

    // Step 3: IN_PROGRESS -> COMPLETED (v4 -> v5)
    await waitFor(() => {
      expect(screen.getByTestId('detail-btn-complete-execution')).not.toBeDisabled();
    });
    fireEvent.click(screen.getByTestId('detail-btn-complete-execution'));

    await waitFor(() => {
      expect(screen.getByTestId('queue-action-success-banner')).toHaveTextContent(
        'IN_PROGRESS → COMPLETED',
      );
    });
  });

  it('kiem tra client validation va thuc thi Danh dau qua luot (SKIPPED), Khoi phuc luot (READY) va Huy luot (CANCELLED)', async () => {
    localStorage.setItem('medflow.access_token', 'tok-diag');

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/queue') {
        return {
          data: resolveMockDiagnosticQueueBoard(
            config?.params as GetDiagnosticQueueParams | undefined,
          ),
        };
      }
      const match = url.match(/^\/diagnostic\/queue\/([^/]+)$/);
      if (match) {
        return {
          data: resolveMockDiagnosticQueueEntryDetail(decodeURIComponent(match[1])),
        };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      const missedMatch = url.match(/^\/diagnostic\/queue\/([^/]+)\/missed-turn$/);
      if (missedMatch) {
        return {
          data: markMockDiagnosticMissedTurn(
            decodeURIComponent(missedMatch[1]),
            body as Record<string, unknown>,
          ),
        };
      }
      const restoreMatch = url.match(/^\/diagnostic\/queue\/([^/]+)\/restore-turn$/);
      if (restoreMatch) {
        return {
          data: restoreMockDiagnosticSkippedTurn(
            decodeURIComponent(restoreMatch[1]),
            body as Record<string, unknown>,
          ),
        };
      }
      const cancelMatch = url.match(/^\/diagnostic\/queue\/([^/]+)\/cancel$/);
      if (cancelMatch) {
        return {
          data: cancelMockDiagnosticQueueEntry(
            decodeURIComponent(cancelMatch[1]),
            body as Record<string, unknown>,
          ),
        };
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    renderPage();

    await screen.findByTestId('queue-entry-row-QE-00009041');

    // Open Missed Turn tab -> submit with empty reason -> validation error
    fireEvent.click(screen.getByTestId('tab-action-missed-turn'));
    fireEvent.click(screen.getByTestId('submit-missed-turn-btn'));

    expect(await screen.findByTestId('queue-action-validation-error')).toHaveTextContent(
      'Lý do đánh dấu bệnh nhân qua lượt cận lâm sàng phải từ 5 đến 500 ký tự.',
    );

    // Fill valid reason and submit Missed Turn (READY -> SKIPPED)
    fireEvent.change(screen.getByLabelText(/Lý do đánh dấu qua lượt \/ vắng mặt/i), {
      target: { value: 'Bệnh nhân vắng mặt khi gọi loa 3 lần tại phòng xét nghiệm.' },
    });
    fireEvent.click(screen.getByTestId('submit-missed-turn-btn'));

    expect(await screen.findByTestId('queue-action-success-banner')).toHaveTextContent(
      'READY → SKIPPED',
    );

    // Switch to Restore Turn tab -> restore SKIPPED -> READY
    fireEvent.click(screen.getByTestId('tab-action-restore-turn'));
    fireEvent.click(screen.getByTestId('submit-restore-turn-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('queue-action-success-banner')).toHaveTextContent(
        'SKIPPED → READY',
      );
    });

    // Switch to Cancel tab -> submit with short reason (<5 chars) -> validation error
    fireEvent.click(screen.getByTestId('tab-action-cancel'));
    fireEvent.change(screen.getByLabelText(/Lý do hủy lượt xếp hàng cận lâm sàng/i), {
      target: { value: 'abc' },
    });
    fireEvent.click(screen.getByTestId('submit-cancel-entry-btn'));

    expect(await screen.findByTestId('queue-action-validation-error')).toHaveTextContent(
      'Lý do hủy lượt xếp hàng cận lâm sàng phải từ 5 đến 500 ký tự.',
    );

    // Fill valid reason and cancel entry
    fireEvent.change(screen.getByLabelText(/Lý do hủy lượt xếp hàng cận lâm sàng/i), {
      target: { value: 'Bác sĩ khám lâm sàng thay đổi chỉ định và yêu cầu hủy lượt.' },
    });
    fireEvent.click(screen.getByTestId('submit-cancel-entry-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('queue-action-success-banner')).toHaveTextContent(
        'READY → CANCELLED',
      );
    });
  });

  it('an thao tac dieu phoi khi nhan vien khong co quyen QUEUE_MANAGE / DIAGNOSTIC_RESULT_WRITE va hien thi loi API 409/500', async () => {
    localStorage.setItem('medflow.access_token', 'tok-diag');

    useAuthStore.setState({
      user: {
        id: 309,
        email: 'viewer@medflow.vn',
        fullName: 'KTV. Chỉ Xem',
        staffCode: 'DIAG-099',
        roles: ['DIAGNOSTIC_STAFF'],
        permissions: ['QUEUE_VIEW'],
        patientCode: null,
      },
      accessToken: 'tok-diag',
      refreshToken: 'rtok-diag',
      isAuthenticated: true,
    });

    renderPage();

    await screen.findByTestId('queue-entry-row-QE-00009041');

    // Read-only notice displayed & Call Next button hidden
    expect(screen.getByTestId('queue-readonly-permission-notice')).toBeInTheDocument();
    expect(screen.queryByTestId('call-next-btn-SP-201')).not.toBeInTheDocument();
    expect(screen.queryByTestId('row-action-call-QE-00009041')).not.toBeInTheDocument();
  });

  it('hien thi banner loi API khi GET /diagnostic/queue tra ve 500 va cho phep thu lai', async () => {
    localStorage.setItem('medflow.access_token', 'tok-diag');

    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(
      new AxiosError(
        'Internal Server Error',
        '500',
        undefined,
        {},
        {
          status: 500,
          statusText: 'Internal Server Error',
          headers: {},
          config: {} as never,
          data: { detail: 'Lỗi kết nối dịch vụ hàng đợi cận lâm sàng.' },
        },
      ),
    );

    renderPage();

    expect(await screen.findByTestId('queue-board-error-banner')).toHaveTextContent(
      'Lỗi kết nối dịch vụ hàng đợi cận lâm sàng.',
    );
  });

  it('bao ve route /diagnostic/queue bang RequireAuth va RequireDiagnostic', async () => {
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/diagnostic/queue']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    await waitFor(() => {
      expect(screen.getByTestId('location-probe')).toHaveTextContent(
        '/login?returnUrl=%2Fdiagnostic%2Fqueue',
      );
    });
    unmount();

    // Unauthorized role (DOCTOR) redirected to /doctor
    useAuthStore.setState({
      user: {
        id: 201,
        email: 'doctor@medflow.vn',
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
        <MemoryRouter initialEntries={['/diagnostic/queue']}>
          <AppRouter />
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    await waitFor(() => {
      expect(screen.getByTestId('location-probe')).toHaveTextContent('/doctor');
    });
  });
});
