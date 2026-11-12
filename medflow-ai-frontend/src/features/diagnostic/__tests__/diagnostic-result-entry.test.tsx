import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import {
  amendMockDiagnosticResult,
  enterMockDiagnosticResult,
  resetDiagnosticResultEntryMockState,
  resolveMockDiagnosticOrdersForResultEntry,
  resolveMockDiagnosticResultEntryDetail,
} from '../data/diagnostic-result-entry-mock';
import { DiagnosticResultEntryPage } from '../pages/diagnostic-result-entry-page';
import type {
  DiagnosticResultEntryDetailResponseDto,
  DiagnosticResultEntryItemDto,
  DiagnosticResultEntryListResponseDto,
  EnterDiagnosticResultRequest,
  GetDiagnosticOrdersForResultEntryParams,
  UpdateDiagnosticResultRequest,
} from '../types/diagnostic.types';

const AMEND_BLOCKED_REASON =
  'Lượt khám đã hoàn tất (COMPLETED), không thể sửa kết quả cận lâm sàng đã khóa.';

function blockAmendOn0041(order: DiagnosticResultEntryItemDto): DiagnosticResultEntryItemDto {
  if (order.orderCode !== 'DIA-202610-0041') {
    return order;
  }
  return {
    ...order,
    canAmendResult: false,
    amendDisabledReason: AMEND_BLOCKED_REASON,
    eligibility: {
      ...order.eligibility,
      canAmendResult: false,
      amendDisabledReason: AMEND_BLOCKED_REASON,
    },
  };
}

function renderPage(initialEntry = '/diagnostic/results') {
  const qc = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <DiagnosticResultEntryPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DiagnosticResultEntryPage (UC43)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    resetDiagnosticResultEntryMockState();

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

  it('render header UC43, 4 stat cards va bang phieu cho nhap ket qua tu mock fallback (khong token)', async () => {
    renderPage();

    expect(await screen.findByText('Nhập & trả kết quả cận lâm sàng')).toBeInTheDocument();
    expect(screen.getByText('UC43')).toBeInTheDocument();
    await screen.findByTestId('result-entry-order-row-DIA-202610-0044');

    const statCards = screen.getByTestId('diagnostic-result-entry-stat-cards');
    expect(statCards).toBeInTheDocument();
    expect(screen.getByTestId('stat-card-ready-for-result-entry')).toHaveTextContent('4');
    expect(screen.getByTestId('stat-card-in-progress-execution')).toHaveTextContent('4');
    expect(screen.getByTestId('stat-card-pending-verification')).toHaveTextContent('1');
    expect(screen.getByTestId('stat-card-finalized')).toHaveTextContent('1');

    const table = screen.getByTestId('diagnostic-result-entry-table');
    expect(table).toBeInTheDocument();
    expect(await screen.findByTestId('result-entry-order-row-DIA-202610-0044')).toBeInTheDocument();
    expect(screen.getByTestId('result-entry-order-row-DIA-202610-0045')).toBeInTheDocument();
    expect(screen.getByTestId('result-entry-order-row-DIA-202610-0046')).toBeInTheDocument();
    expect(screen.getByTestId('result-entry-order-row-DIA-202610-0041')).toBeInTheDocument();
    expect(screen.getByTestId('result-entry-order-row-DIA-202610-0047')).toBeInTheDocument();
    expect(screen.getByTestId('result-entry-order-row-DIA-202610-0048')).toBeInTheDocument();
    expect(screen.getByTestId('result-entry-order-row-DIA-202610-0049')).toBeInTheDocument();
  });

  it('chon phieu IN_PROGRESS hien thi detail panel voi checklist dieu kien va form nhap ket qua', async () => {
    renderPage();

    // First canEnterResult item (DIA-202610-0044) auto-selected after list resolves
    expect(await screen.findByTestId('diagnostic-result-entry-detail-panel')).toBeInTheDocument();
    expect(await screen.findByTestId('result-entry-eligibility-checklist')).toBeInTheDocument();
    expect(screen.getByTestId('result-entry-eligibility-checklist')).toHaveTextContent(
      'Đạt tiêu chí nhập KQ',
    );
    expect(screen.getByTestId('result-entry-form')).toBeInTheDocument();
    expect(await screen.findByText('E11.65')).toBeInTheDocument();

    // Select the DRAFT-version order → version history + pre-filled form
    fireEvent.click(screen.getByTestId('result-entry-order-row-DIA-202610-0045'));

    expect(await screen.findByTestId('result-version-history')).toBeInTheDocument();
    expect(screen.getByTestId('result-version-1')).toBeInTheDocument();

    const resultTextArea = screen.getByLabelText(
      'Nội dung kết quả cận lâm sàng',
    ) as HTMLTextAreaElement;
    expect(resultTextArea.value).toContain('ngoại tâm thu thất rải rác');
  });

  it('chan submit khi resultText trong — validation phia client, khong goi API', async () => {
    const postSpy = vi.spyOn(apiClient, 'post');
    renderPage();

    await screen.findByTestId('result-entry-order-row-DIA-202610-0044');
    await screen.findByTestId('result-entry-form');

    const saveButton = screen.getByRole('button', { name: /Lưu kết quả/i });
    fireEvent.click(saveButton);

    expect(await screen.findByTestId('result-form-validation-error')).toHaveTextContent(
      'Vui lòng nhập nội dung kết quả cận lâm sàng.',
    );
    expect(postSpy).not.toHaveBeenCalled();

    // Finalize without conclusion → conclusion validation error
    const finalizeButton = screen.getByRole('button', { name: /Duyệt & trả kết quả/i });
    fireEvent.change(screen.getByLabelText('Nội dung kết quả cận lâm sàng'), {
      target: { value: 'Kết quả sinh hóa: Glucose 14.1 mmol/L, HbA1c 9.2%.' },
    });
    fireEvent.click(finalizeButton);

    expect(await screen.findByTestId('result-form-validation-error')).toHaveTextContent(
      'Vui lòng nhập kết luận trước khi duyệt & trả kết quả.',
    );
    expect(postSpy).not.toHaveBeenCalled();
  });

  it('luu ket qua hop le tao phien ban moi va hien thi banner thanh cong + cap nhat bang', async () => {
    renderPage();

    await screen.findByTestId('result-entry-order-row-DIA-202610-0044');
    await screen.findByTestId('result-entry-form');

    fireEvent.change(screen.getByLabelText('Nội dung kết quả cận lâm sàng'), {
      target: {
        value:
          'Sinh hóa máu: Glucose đói 14.1 mmol/L (tăng cao), HbA1c 9.2%, Creatinin 78 µmol/L trong giới hạn.',
      },
    });
    fireEvent.change(screen.getByLabelText('Kết luận chẩn đoán cận lâm sàng'), {
      target: { value: 'Đường huyết mất bù trên nền đái tháo đường type 2.' },
    });

    const saveButton = screen.getByRole('button', { name: /Lưu kết quả/i });
    fireEvent.click(saveButton);

    const successBanner = await screen.findByTestId('result-entry-success');
    expect(successBanner).toHaveTextContent('DIA-202610-0044');
    expect(successBanner).toHaveTextContent('v1');
    expect(successBanner).toHaveTextContent('DRAFT');
    expect(successBanner).toHaveTextContent('RESULT_SAVED');

    // Version history appears in detail panel after invalidate + refetch
    expect(await screen.findByTestId('result-version-history')).toBeInTheDocument();
    expect(await screen.findByTestId('result-version-1')).toHaveTextContent(
      'Đường huyết mất bù trên nền đái tháo đường type 2.',
    );
  });

  it('duyet & tra ket qua tren phieu PENDING_VERIFY chuyen sang FINALIZED/VERIFIED va khoa form', async () => {
    renderPage();

    fireEvent.click(await screen.findByTestId('result-entry-order-row-DIA-202610-0046'));

    // PENDING_VERIFY order pre-fills result text/conclusion (latest version)
    await screen.findByTestId('result-entry-form');

    const finalizeButton = screen.getByRole('button', { name: /Duyệt & trả kết quả/i });
    fireEvent.click(finalizeButton);

    const successBanner = await screen.findByTestId('result-entry-success');
    expect(successBanner).toHaveTextContent('DIA-202610-0046');
    expect(successBanner).toHaveTextContent('RESULT_FINALIZED');
    expect(successBanner).toHaveTextContent('VERIFIED');

    // After invalidation + refetch the order becomes finalized → lock banner + disabled buttons
    expect(await screen.findByTestId('result-finalized-lock-banner')).toBeInTheDocument();

    const rowBadge = await screen.findByTestId('result-entry-order-row-DIA-202610-0046');
    await waitFor(() => {
      expect(rowBadge).toHaveTextContent('Đã thẩm định');
    });
  });

  it('phieu da FINALIZED mo form sua ket qua da khoa va giu nut nhap moi tat', async () => {
    renderPage();

    fireEvent.click(await screen.findByTestId('result-entry-order-row-DIA-202610-0041'));

    expect(await screen.findByTestId('result-finalized-lock-banner')).toBeInTheDocument();
    expect(screen.getByTestId('result-amend-form')).toBeInTheDocument();
    expect(screen.queryByTestId('result-entry-form')).not.toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: /DRAFT/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Sửa kết quả đã khóa/i })).toBeEnabled();
    expect(screen.getByTestId('result-entry-order-row-DIA-202610-0041')).toHaveTextContent(
      'Sửa KQ',
    );
  });

  it('goi GET /diagnostic/results, GET /diagnostic/results/{code} va POST /diagnostic/orders/{code}/result khi co token', async () => {
    localStorage.setItem('medflow.access_token', 'valid-diagnostic-jwt-token');

    const getSpy = vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/results') {
        return {
          data: resolveMockDiagnosticOrdersForResultEntry(
            config?.params as GetDiagnosticOrdersForResultEntryParams | undefined,
          ),
        };
      }
      if (typeof url === 'string' && url.startsWith('/diagnostic/results/')) {
        const code = decodeURIComponent(url.split('/')[3]);
        return { data: resolveMockDiagnosticResultEntryDetail(code) };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      if (typeof url === 'string' && url.endsWith('/result')) {
        const code = decodeURIComponent(url.split('/')[3]);
        return {
          data: enterMockDiagnosticResult(code, body as EnterDiagnosticResultRequest),
        };
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    renderPage();

    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith('/diagnostic/results', expect.anything());
    });

    await screen.findByTestId('result-entry-order-row-DIA-202610-0044');
    await screen.findByTestId('result-entry-form');

    fireEvent.change(screen.getByLabelText('Nội dung kết quả cận lâm sàng'), {
      target: { value: 'Sinh hóa máu: Glucose đói 14.1 mmol/L, HbA1c 9.2%.' },
    });
    fireEvent.change(screen.getByLabelText('Kết luận chẩn đoán cận lâm sàng'), {
      target: { value: 'Đường huyết mất bù trên nền đái tháo đường type 2.' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Lưu kết quả/i }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        '/diagnostic/orders/DIA-202610-0044/result',
        expect.objectContaining({
          orderId: 44,
          resultText: 'Sinh hóa máu: Glucose đói 14.1 mmol/L, HbA1c 9.2%.',
          conclusion: 'Đường huyết mất bù trên nền đái tháo đường type 2.',
          status: 'DRAFT',
          isFinalized: false,
          expectedStateVersion: 3,
        }),
      );
    });

    const successBanner = await screen.findByTestId('result-entry-success');
    expect(successBanner).toHaveTextContent('RESULT_SAVED');
    expect(successBanner).toHaveTextContent('v1');
  });

  it('chan sua ket qua da khoa khi thieu ly do — khong tao version moi', async () => {
    renderPage();

    fireEvent.click(await screen.findByTestId('result-entry-order-row-DIA-202610-0041'));
    await screen.findByTestId('result-amend-form');

    fireEvent.change(screen.getByLabelText('Kết luận chẩn đoán cận lâm sàng'), {
      target: { value: 'Giảm tiểu cầu nhẹ, đề nghị đếm lại sau 24 giờ.' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Sửa kết quả đã khóa/i }));

    expect(await screen.findByTestId('result-form-validation-error')).toHaveTextContent(
      'Lý do sửa kết quả cận lâm sàng (CorrectionReason) phải có ít nhất 5 ký tự.',
    );
    expect(screen.queryByTestId('result-version-2')).not.toBeInTheDocument();
  });

  it('sua ket qua da khoa tao version moi, giu nguoi duyet goc va outcome AMENDED', async () => {
    renderPage();

    fireEvent.click(await screen.findByTestId('result-entry-order-row-DIA-202610-0041'));
    await screen.findByTestId('result-amend-form');

    fireEvent.change(screen.getByLabelText('Kết luận chẩn đoán cận lâm sàng'), {
      target: { value: 'Giảm tiểu cầu nhẹ, đề nghị đếm lại tiểu cầu sau 24 giờ.' },
    });
    fireEvent.change(screen.getByLabelText('Lý do sửa kết quả đã khóa'), {
      target: { value: 'Đính chính khuyến nghị theo mẫu chạy lại.' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Sửa kết quả đã khóa/i }));

    const successBanner = await screen.findByTestId('result-entry-success');
    expect(successBanner).toHaveTextContent('DIAGNOSTIC_RESULT_AMENDED');
    expect(successBanner).toHaveTextContent('v2');
    expect(successBanner).toHaveTextContent('v7');

    const lockBanner = screen.getByTestId('result-finalized-lock-banner');
    expect(lockBanner).toHaveTextContent('CN. Lê Hoàng Minh');

    const version2 = await screen.findByTestId('result-version-2');
    expect(version2).toHaveTextContent('Đính chính khuyến nghị theo mẫu chạy lại.');
    expect(version2).toHaveTextContent('Giảm tiểu cầu nhẹ, đề nghị đếm lại tiểu cầu sau 24 giờ.');

    expect(screen.getByTestId('result-entry-order-row-DIA-202610-0041')).toHaveTextContent(
      'Đã thẩm định',
    );
  });

  it('tu choi sua khi noi dung va file khong doi', async () => {
    renderPage();

    fireEvent.click(await screen.findByTestId('result-entry-order-row-DIA-202610-0041'));
    await screen.findByTestId('result-amend-form');

    fireEvent.change(screen.getByLabelText('Lý do sửa kết quả đã khóa'), {
      target: { value: 'Chỉ bổ sung lý do, không đổi nội dung.' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Sửa kết quả đã khóa/i }));

    expect(await screen.findByTestId('result-entry-error-banner')).toHaveTextContent(
      'Không có thay đổi nội dung kết quả cận lâm sàng.',
    );
    expect(screen.queryByTestId('result-version-2')).not.toBeInTheDocument();
  });

  it('xoa trang duong dan tep thi version moi khong con file cu', async () => {
    renderPage();

    fireEvent.click(await screen.findByTestId('result-entry-order-row-DIA-202610-0041'));
    await screen.findByTestId('result-amend-form');

    fireEvent.change(screen.getByLabelText('Kết luận chẩn đoán cận lâm sàng'), {
      target: { value: 'Giảm tiểu cầu nhẹ, theo dõi thêm sau 24 giờ.' },
    });
    fireEvent.change(screen.getByLabelText('Đường dẫn tệp kết quả đính kèm'), {
      target: { value: '' },
    });
    fireEvent.change(screen.getByLabelText('Lý do sửa kết quả đã khóa'), {
      target: { value: 'Gỡ file PDF đính kèm sai phiếu.' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Sửa kết quả đã khóa/i }));

    const version2 = await screen.findByTestId('result-version-2');
    expect(version2).toHaveTextContent('Không có tệp đính kèm');
    expect(version2).not.toHaveTextContent('cbc-DIA-202610-0041.pdf');
  });

  it('co token thi POST /result/amend va khong goi POST /result', async () => {
    localStorage.setItem('medflow.access_token', 'valid-diagnostic-jwt-token');

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/results') {
        return {
          data: resolveMockDiagnosticOrdersForResultEntry(
            config?.params as GetDiagnosticOrdersForResultEntryParams | undefined,
          ),
        };
      }
      if (typeof url === 'string' && url.startsWith('/diagnostic/results/')) {
        const code = decodeURIComponent(url.split('/')[3]);
        return { data: resolveMockDiagnosticResultEntryDetail(code) };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      if (typeof url === 'string' && url.endsWith('/result/amend')) {
        const code = decodeURIComponent(url.split('/')[3]);
        return {
          data: amendMockDiagnosticResult(code, body as UpdateDiagnosticResultRequest),
        };
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('result-entry-order-row-DIA-202610-0041'));
    await screen.findByTestId('result-amend-form');

    fireEvent.change(screen.getByLabelText('Kết luận chẩn đoán cận lâm sàng'), {
      target: { value: 'Giảm tiểu cầu nhẹ, đếm lại sau 24 giờ.' },
    });
    fireEvent.change(screen.getByLabelText('Lý do sửa kết quả đã khóa'), {
      target: { value: 'Sửa kết luận theo kết quả chạy lại.' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Sửa kết quả đã khóa/i }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledTimes(1);
      expect(postSpy).toHaveBeenCalledWith(
        '/diagnostic/orders/DIA-202610-0041/result/amend',
        expect.objectContaining({
          orderId: 41,
          conclusion: 'Giảm tiểu cầu nhẹ, đếm lại sau 24 giờ.',
          correctionReason: 'Sửa kết luận theo kết quả chạy lại.',
          reportFileUrl: null,
          expectedStateVersion: 6,
        }),
      );
    });

    const calledUrls = postSpy.mock.calls.map((call) => call[0]);
    expect(calledUrls.some((url) => typeof url === 'string' && url.endsWith('/result'))).toBe(
      false,
    );
  });

  it('an nut sua khi server tra canAmendResult false', async () => {
    localStorage.setItem('medflow.access_token', 'valid-diagnostic-jwt-token');

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/results') {
        const list = resolveMockDiagnosticOrdersForResultEntry(
          config?.params as GetDiagnosticOrdersForResultEntryParams | undefined,
        );
        const blocked: DiagnosticResultEntryListResponseDto = {
          ...list,
          items: list.items.map(blockAmendOn0041),
        };
        return { data: blocked };
      }
      if (typeof url === 'string' && url.startsWith('/diagnostic/results/')) {
        const code = decodeURIComponent(url.split('/')[3]);
        const detail = resolveMockDiagnosticResultEntryDetail(code);
        const blocked: DiagnosticResultEntryDetailResponseDto = {
          ...detail,
          order: blockAmendOn0041(detail.order),
          versions: detail.versions,
        };
        return { data: blocked };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('result-entry-order-row-DIA-202610-0041'));

    expect(await screen.findByTestId('result-amend-disabled-reason')).toHaveTextContent(
      AMEND_BLOCKED_REASON,
    );
    expect(screen.queryByRole('button', { name: /Sửa kết quả đã khóa/i })).not.toBeInTheDocument();
    expect(screen.queryByTestId('result-amend-form')).not.toBeInTheDocument();
  });
});
