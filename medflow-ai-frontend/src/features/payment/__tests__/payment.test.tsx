import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { AppointmentListItem } from '@/features/appointment/types/appointment-list.types';
import { apiClient } from '@/shared/lib/axios';

import { PatientPaymentDetailPage } from '../pages/patient-payment-detail-page';
import { PatientPaymentListPage } from '../pages/patient-payment-list-page';
import type {
  GenerateQrPaymentResponse,
  VisitChargeLine,
  VisitChargesResponse,
} from '../types/payment.types';

vi.mock('@/shared/components/layout/app-header', () => ({
  AppHeader: () => <div data-testid="app-header" />,
}));
vi.mock('@/shared/components/layout/patient-footer', () => ({
  PatientFooter: () => <div data-testid="patient-footer" />,
}));

const charge = (overrides: Partial<VisitChargeLine> = {}): VisitChargeLine => ({
  chargeId: 1,
  chargeSource: 'PERSISTED',
  serviceId: 1,
  serviceCode: 'DV-01',
  serviceName: 'Khám Nội tổng quát',
  diagnosticOrderId: null,
  unitPrice: 200000,
  paymentRequirement: 'REQUIRED',
  payable: true,
  chargeDataStatus: 'VALID',
  ...overrides,
});

const charges = (overrides: Partial<VisitChargesResponse> = {}): VisitChargesResponse => ({
  visitId: 1,
  visitCode: 'VIS-1',
  visitStatus: 'COMPLETED',
  patientId: 1,
  charges: [charge()],
  payableTotal: 290000,
  hasUnpricedPayableCharges: false,
  amountDue: 290000,
  currency: 'VND',
  recognizedPaidTotal: 0,
  settlementStatus: 'UNPAID',
  paymentId: null,
  paymentCode: null,
  paymentStatus: 'UNPAID',
  paymentAmount: null,
  paymentMethod: null,
  paidAt: null,
  externalReference: null,
  canPayByQr: true,
  ...overrides,
});

const qrResponse = (
  overrides: Partial<GenerateQrPaymentResponse> = {},
): GenerateQrPaymentResponse => ({
  visitId: 1,
  visitCode: 'VIS-1',
  paymentId: 42,
  paymentCode: 'MF42',
  amount: 290000,
  currency: 'VND',
  status: 'PENDING',
  qrPayload: '00020101021238570010A000000727',
  bankBin: '970422',
  accountNumber: '0123456789',
  accountName: 'BENH VIEN DEMO',
  transferContent: 'MF42',
  issueKind: 'CREATED',
  ...overrides,
});

const appointment = (overrides: Partial<AppointmentListItem>): AppointmentListItem =>
  ({
    id: '1',
    appointmentCode: 'APT-202610-0001',
    visitId: 1,
    visitCode: 'VIS-1',
    doctorName: 'BS. Nguyễn Văn A',
    serviceName: 'Khám Nội tổng quát',
    scheduledDateFullLabel: 'Thứ Bảy, 14/11/2026 (Ca Sáng)',
    scheduledTimeRange: '09:00 - 09:30',
    paymentStatusLabel: 'Chờ thanh toán tại quầy',
    ...overrides,
  }) as AppointmentListItem;

function renderWithProviders(ui: React.ReactElement, initialPath: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route path="/payments" element={<div>payment-list</div>} />
          <Route path="/payments/:visitCode" element={ui} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function renderList() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/payments']}>
        <PatientPaymentListPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function problemError(status: number, detail: string) {
  return new AxiosError('request failed', 'ERR_BAD_REQUEST', undefined, undefined, {
    status,
    statusText: 'error',
    headers: {},
    config: {},
    data: { status, title: 'Lỗi', detail },
  } as never);
}

afterEach(() => {
  vi.restoreAllMocks();
  window.localStorage.clear();
});

describe('PatientPaymentListPage', () => {
  it('chỉ liệt kê lượt khám đã tiếp nhận', async () => {
    window.localStorage.setItem('medflow.access_token', 'test-token');
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        items: [
          appointment({ appointmentCode: 'APT-VISITED', visitCode: 'VIS-1' }),
          appointment({ id: '2', appointmentCode: 'APT-NOT-CHECKED-IN', visitCode: null }),
        ],
        total: 2,
        page: 1,
        pageSize: 50,
        totalPages: 1,
        tabCounts: { all: 2, upcoming: 2, completed: 0, cancelled: 0 },
        memberCounts: [],
        totalAllMembersCount: 2,
      },
    } as never);

    renderList();

    expect(await screen.findByText('APT-VISITED')).toBeInTheDocument();
    expect(screen.queryByText('APT-NOT-CHECKED-IN')).not.toBeInTheDocument();
    expect(screen.getByText('Xem viện phí')).toBeInTheDocument();
  });

  it('hiện thông báo khi chưa có lượt khám nào', async () => {
    window.localStorage.setItem('medflow.access_token', 'test-token');
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        items: [],
        total: 0,
        page: 1,
        pageSize: 50,
        totalPages: 0,
        tabCounts: { all: 0, upcoming: 0, completed: 0, cancelled: 0 },
        memberCounts: [],
        totalAllMembersCount: 0,
      },
    } as never);

    renderList();

    expect(await screen.findByText(/Chưa có lượt khám nào đã tiếp nhận/)).toBeInTheDocument();
  });
});

describe('PatientPaymentDetailPage', () => {
  it('không cho tạo QR khi số tiền chưa xác định', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: charges({ amountDue: null, canPayByQr: false }),
    } as never);

    renderWithProviders(<PatientPaymentDetailPage />, '/payments/VIS-1');

    expect(await screen.findByText('Chưa xác định')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Tạo mã VietQR' })).not.toBeInTheDocument();
  });

  it('tạo mã QR và hiển thị nội dung chuyển khoản', async () => {
    const user = userEvent.setup();
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: charges() } as never);
    const post = vi.spyOn(apiClient, 'post').mockResolvedValue({ data: qrResponse() } as never);

    renderWithProviders(<PatientPaymentDetailPage />, '/payments/VIS-1');

    await user.click(await screen.findByRole('button', { name: 'Tạo mã VietQR' }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(post.mock.calls[0]?.[0]).toBe('/patients/visits/VIS-1/payments/qr');
    expect(await screen.findByText('MF42')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Mã VietQR MF42' })).toBeInTheDocument();
    expect(screen.getByText('Nội dung chuyển khoản')).toBeInTheDocument();
  });

  it('giữ nguyên thông điệp 409 từ backend và cho bấm lại', async () => {
    const user = userEvent.setup();
    const detail = 'Lượt khám được thu tại quầy.';
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: charges() } as never);
    vi.spyOn(apiClient, 'post').mockRejectedValue(problemError(409, detail));

    renderWithProviders(<PatientPaymentDetailPage />, '/payments/VIS-1');

    await user.click(await screen.findByRole('button', { name: 'Tạo mã VietQR' }));

    expect(await screen.findByText(detail)).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Tạo mã VietQR' })).toBeEnabled();
  });

  it('hiển thị trạng thái đã thanh toán và không còn khối chờ', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: charges({
        settlementStatus: 'PAID',
        recognizedPaidTotal: 290000,
        amountDue: 0,
        canPayByQr: false,
        paymentId: 42,
        paymentCode: 'MF42',
        paymentStatus: 'PAID',
        paymentAmount: 290000,
      }),
    } as never);

    renderWithProviders(<PatientPaymentDetailPage />, '/payments/VIS-1');

    expect(await screen.findByText(/đã được thanh toán đủ/)).toBeInTheDocument();
    expect(screen.queryByText('Đang chờ thanh toán...')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Tạo mã VietQR' })).not.toBeInTheDocument();
  });

  it('đánh dấu dòng miễn thu, chưa có giá và dữ liệu phí không hợp lệ', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: charges({
        charges: [
          charge({ chargeId: 1, payable: false, unitPrice: 50000, serviceName: 'Khám miễn thu' }),
          charge({
            chargeId: 2,
            chargeDataStatus: 'UNPRICED',
            unitPrice: null,
            serviceName: 'Xét nghiệm chưa có giá',
          }),
          charge({
            chargeId: 3,
            chargeDataStatus: 'INVALID',
            payable: null,
            unitPrice: -1,
            serviceName: 'Dòng phí hỏng',
          }),
        ],
      }),
    } as never);

    renderWithProviders(<PatientPaymentDetailPage />, '/payments/VIS-1');

    expect(await screen.findByText('Miễn thu')).toBeInTheDocument();
    expect(screen.getByText('Chưa có giá')).toBeInTheDocument();
    expect(screen.getByText('Dữ liệu phí không hợp lệ')).toBeInTheDocument();
  });

  it('hiện lỗi tải viện phí kèm nút thử lại', async () => {
    const detail = 'Bạn không có quyền xem khoản phí của lượt khám này.';
    vi.spyOn(apiClient, 'get').mockRejectedValue(problemError(403, detail));

    renderWithProviders(<PatientPaymentDetailPage />, '/payments/VIS-1');

    expect(await screen.findByText(detail)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Thử lại/ })).toBeInTheDocument();
  });
});
