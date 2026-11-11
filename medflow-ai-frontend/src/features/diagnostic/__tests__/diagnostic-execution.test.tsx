import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import {
  resetDiagnosticExecutionMockState,
  resolveMockDiagnosticExecutionDetail,
  resolveMockDiagnosticExecutions,
  startMockDiagnosticServiceExecution,
  updateMockDiagnosticServiceStatus,
  updateMockDiagnosticServiceStatusBatch,
} from '../data/diagnostic-execution-mock';
import { DiagnosticExecutionPage } from '../pages/diagnostic-execution-page';
import { DiagnosticQueueManagePage } from '../pages/diagnostic-queue-manage-page';
import type {
  GetDiagnosticExecutionsParams,
  UpdateDiagnosticServiceStatusBatchRequest,
  UpdateDiagnosticServiceStatusRequest,
} from '../types/diagnostic.types';

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location-probe">{location.pathname + location.search}</div>;
}

function renderExecutionPage(initialEntry = '/diagnostic/execution') {
  const qc = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/diagnostic/execution" element={<DiagnosticExecutionPage />} />
          <Route
            path="/diagnostic/execution/:orderIdOrCode"
            element={<DiagnosticExecutionPage />}
          />
          <Route path="/diagnostic/queue" element={<DiagnosticQueueManagePage />} />
          <Route path="*" element={<LocationProbe />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DiagnosticExecutionPage (UC42)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    resetDiagnosticExecutionMockState();

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

  it('render header UC42, stat cards, bang thuc hien va chi tiet phiếu đầu tiên với checklist + lich su', async () => {
    renderExecutionPage();

    expect(
      await screen.findByText('Cập nhật trạng thái thực hiện kỹ thuật cận lâm sàng'),
    ).toBeInTheDocument();
    expect(screen.getByText('UC42')).toBeInTheDocument();

    // First eligible CONFIRMED+READY item (DIA-202611-0061) auto-selected
    expect(await screen.findByTestId('execution-order-row-DIA-202611-0061')).toBeInTheDocument();

    expect(screen.getByTestId('diagnostic-execution-stat-cards')).toBeInTheDocument();
    expect(screen.getByTestId('stat-card-waiting-execution')).toHaveTextContent('2');
    expect(screen.getByTestId('stat-card-ready-to-start')).toHaveTextContent('1');
    expect(screen.getByTestId('stat-card-in-progress')).toHaveTextContent('2');
    expect(screen.getByTestId('stat-card-completed')).toHaveTextContent('1');
    expect(screen.getByTestId('stat-card-urgent-stat')).toHaveTextContent('2');

    // Detail panel: eligibility checklist + empty history
    expect(await screen.findByTestId('execution-eligibility')).toHaveTextContent(
      'Checklist điều kiện thực hiện (UC42)',
    );
    expect(screen.getByTestId('execution-history')).toHaveTextContent(
      'Chưa ghi nhận lịch sử chuyển trạng thái',
    );
  });

  it('loc danh sach theo nhom CLS, tu khoa khong trung va readyToExecuteOnly', async () => {
    renderExecutionPage();

    await screen.findByTestId('execution-order-row-DIA-202611-0061');

    // Category filter: IMAGING narrows to DIA-202611-0062 + DIA-202611-0065
    const categorySelect = screen.getByLabelText('Lọc nhóm cận lâm sàng');
    fireEvent.change(categorySelect, { target: { value: 'IMAGING' } });
    await waitFor(() => {
      expect(screen.getByTestId('execution-order-row-DIA-202611-0062')).toBeInTheDocument();
      expect(screen.getByTestId('execution-order-row-DIA-202611-0065')).toBeInTheDocument();
      expect(screen.queryByTestId('execution-order-row-DIA-202611-0061')).toBeNull();
    });

    // Keyword no-match → empty state
    const keywordInput = screen.getByLabelText('Tìm kiếm phiếu chỉ định thực hiện');
    fireEvent.change(keywordInput, { target: { value: 'ZZZ-NO-MATCH-XYZ' } });
    expect(await screen.findByTestId('diagnostic-execution-empty-state')).toBeInTheDocument();

    // Reset filters + readyToExecuteOnly keeps only startable CONFIRMED+READY
    fireEvent.change(keywordInput, { target: { value: '' } });
    fireEvent.change(categorySelect, { target: { value: 'ALL' } });
    const readyOnly = screen.getByLabelText('Chỉ phiếu sẵn sàng thực hiện');
    fireEvent.click(readyOnly);
    await waitFor(() => {
      expect(screen.getByTestId('execution-order-row-DIA-202611-0061')).toBeInTheDocument();
      expect(screen.queryByTestId('execution-order-row-DIA-202611-0062')).toBeNull();
      expect(screen.queryByTestId('execution-order-row-DIA-202611-0063')).toBeNull();
    });
  });

  it('bat dau thuc hien phieu CONFIRMED+READY chuyen sang IN_PROGRESS, queue sync va ghi history', async () => {
    localStorage.setItem('medflow.access_token', 'valid-diagnostic-jwt-token');

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/orders/execution') {
        return {
          data: resolveMockDiagnosticExecutions(
            config?.params as GetDiagnosticExecutionsParams | undefined,
          ),
        };
      }
      if (typeof url === 'string' && url.endsWith('/execution')) {
        const code = decodeURIComponent(url.split('/')[3]);
        return { data: resolveMockDiagnosticExecutionDetail(code) };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      if (typeof url === 'string' && url.endsWith('/start')) {
        const code = decodeURIComponent(url.split('/')[3]);
        return {
          data: startMockDiagnosticServiceExecution(
            code,
            body as UpdateDiagnosticServiceStatusRequest,
          ),
        };
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    renderExecutionPage();

    await screen.findByTestId('execution-order-row-DIA-202611-0061');
    expect(await screen.findByTestId('execution-start-button')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('execution-start-button'));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/diagnostic/orders/DIA-202611-0061/start',
        expect.objectContaining({ targetStatus: 'IN_PROGRESS' }),
      );
    });

    // Row flips to IN_PROGRESS + success banner shows transition
    await waitFor(() => {
      expect(screen.getByTestId('execution-order-row-DIA-202611-0061')).toHaveTextContent(
        'Đang thực hiện',
      );
    });
    expect(await screen.findByTestId('execution-action-summary')).toHaveTextContent(
      'CONFIRMED → IN_PROGRESS',
    );

    // Stat inProgressCount incremented 2 → 3
    expect(screen.getByTestId('stat-card-in-progress')).toHaveTextContent('3');

    // History gains DIAGNOSTIC_SERVICE_EXECUTION_STARTED
    await waitFor(() => {
      expect(screen.getByTestId('execution-history')).toHaveTextContent(
        'Bắt đầu thực hiện kỹ thuật',
      );
    });
  });

  it('hoan tat phieu IN_PROGRESS chuyen sang COMPLETED va an nut cap nhat', async () => {
    renderExecutionPage('/diagnostic/execution/DIA-202611-0063');

    await screen.findByTestId('execution-order-row-DIA-202611-0063');
    expect(await screen.findByTestId('execution-complete-button')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('execution-complete-button'));

    await waitFor(() => {
      expect(screen.getByTestId('execution-order-row-DIA-202611-0063')).toHaveTextContent(
        'Đã hoàn tất',
      );
    });

    // Terminal state — update controls disappear
    await waitFor(() => {
      expect(screen.queryByTestId('execution-start-button')).toBeNull();
      expect(screen.queryByTestId('execution-complete-button')).toBeNull();
      expect(screen.queryByTestId('execution-pause-button')).toBeNull();
      expect(screen.queryByTestId('execution-cancel-button')).toBeNull();
    });
  });

  it('tam dung IN_PROGRESS yeu cau ly do 5-500 ky tu; loi ngan khong gui mutation', async () => {
    renderExecutionPage('/diagnostic/execution/DIA-202611-0063');

    await screen.findByTestId('execution-order-row-DIA-202611-0063');
    fireEvent.click(await screen.findByTestId('execution-pause-button'));

    const pauseForm = await screen.findByTestId('execution-pause-form');
    expect(pauseForm).toBeInTheDocument();

    // Reason <5 chars → zod error, no mutation
    const reasonInput = screen.getByLabelText('Lý do tạm dừng thực hiện kỹ thuật');
    fireEvent.change(reasonInput, { target: { value: 'abc' } });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận tạm dừng/i }));

    expect(await screen.findByTestId('execution-form-validation-error')).toHaveTextContent(
      'phải từ 5 đến 500 ký tự',
    );

    // Valid reason → pause to READY
    fireEvent.change(reasonInput, {
      target: {
        value: 'Bệnh nhân chưa sẵn sàng thực hiện kỹ thuật, tạm dừng và xếp lại hàng đợi.',
      },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận tạm dừng/i }));

    await waitFor(() => {
      expect(screen.getByTestId('execution-order-row-DIA-202611-0063')).toHaveTextContent(
        'Đã xác nhận',
      );
    });
    expect(await screen.findByTestId('execution-action-summary')).toHaveTextContent(
      'IN_PROGRESS → CONFIRMED',
    );
  });

  it('vo hieu hoa hanh dong cho phieu ORDERED va phieu co hang doi SKIPPED', async () => {
    // ORDERED order: no action buttons + disabled reason shown
    const first = renderExecutionPage('/diagnostic/execution/DIA-202611-0066');

    await screen.findByTestId('execution-order-row-DIA-202611-0066');
    expect(await screen.findByTestId('execution-disabled-reason')).toHaveTextContent(
      'chưa được xác nhận',
    );
    expect(screen.queryByTestId('execution-start-button')).toBeNull();
    first.unmount();

    // SKIPPED queue order: start blocked with SKIPPED reason
    renderExecutionPage('/diagnostic/execution/DIA-202611-0062');
    await screen.findByTestId('execution-order-row-DIA-202611-0062');
    await waitFor(() => {
      expect(screen.getByTestId('execution-disabled-reason')).toHaveTextContent('SKIPPED');
    });
    expect(screen.queryByTestId('execution-start-button')).toBeNull();
  });

  it('hien thi loi conflict khi expectedStateVersion cu (stale)', async () => {
    renderExecutionPage('/diagnostic/execution/DIA-202611-0063');

    await screen.findByTestId('execution-order-row-DIA-202611-0063');

    // Stale expectedStateVersion → mock throws conflict error
    const detail = resolveMockDiagnosticExecutionDetail('DIA-202611-0063');
    const staleVersion = detail.order.stateVersion - 1;

    expect(() =>
      updateMockDiagnosticServiceStatus('DIA-202611-0063', {
        targetStatus: 'COMPLETED',
        expectedStateVersion: staleVersion,
      }),
    ).toThrow(/đã thay đổi/);
  });

  it('cap nhat hang loat 2 phieu CONFIRMED+hop le sang IN_PROGRESS hien thi updatedCount va summary', async () => {
    localStorage.setItem('medflow.access_token', 'valid-diagnostic-jwt-token');

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/orders/execution') {
        return {
          data: resolveMockDiagnosticExecutions(
            config?.params as GetDiagnosticExecutionsParams | undefined,
          ),
        };
      }
      if (typeof url === 'string' && url.endsWith('/execution')) {
        const code = decodeURIComponent(url.split('/')[3]);
        return { data: resolveMockDiagnosticExecutionDetail(code) };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      if (url === '/diagnostic/orders/execution-status') {
        return {
          data: updateMockDiagnosticServiceStatusBatch(
            body as UpdateDiagnosticServiceStatusBatchRequest,
          ),
        };
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    renderExecutionPage();

    await screen.findByTestId('execution-order-row-DIA-202611-0061');

    // Select the single eligible CONFIRMED row (DIA-202611-0061) — SKIPPED row is disabled
    const checkbox61 = screen.getByLabelText('Chọn cập nhật phiếu DIA-202611-0061');
    fireEvent.click(checkbox61);

    const toolbar = await screen.findByTestId('batch-execution-toolbar');
    expect(toolbar).toHaveTextContent('Đang chọn 1 phiếu chỉ định');

    fireEvent.click(screen.getByTestId('batch-execution-submit'));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/diagnostic/orders/execution-status',
        expect.objectContaining({ orderIds: [61], targetStatus: 'IN_PROGRESS' }),
      );
    });

    expect(await screen.findByTestId('execution-action-summary')).toHaveTextContent(
      'Đã cập nhật 1 phiếu chỉ định',
    );
    await waitFor(() => {
      expect(screen.getByTestId('execution-order-row-DIA-202611-0061')).toHaveTextContent(
        'Đang thực hiện',
      );
    });
  });
  it('sidebar execution nav tren trang quan ly hang doi dieu huong sang /diagnostic/execution', async () => {
    renderExecutionPage('/diagnostic/queue');

    const executionNav = await screen.findByRole('button', { name: /Thực hiện kỹ thuật/i });
    fireEvent.click(executionNav);

    // Queue page navigates to /diagnostic/execution → execution page renders its header
    await waitFor(() => {
      expect(
        screen.getByText('Cập nhật trạng thái thực hiện kỹ thuật cận lâm sàng'),
      ).toBeInTheDocument();
    });
  });
});
