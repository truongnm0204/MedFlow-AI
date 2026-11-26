import { HubConnectionState } from '@microsoft/signalr';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';
import {
  resetRealtimeClientForTests,
  setRealtimeConnectionFactoryForTests,
  type HubConnectionLike,
} from '@/shared/lib/realtime-client';

import { VisitPaymentPage } from '../pages/visit-payment-page';
import type {
  DeclineDiagnosticOrderResponse,
  QrPaymentResponseDto,
  VisitChargesResponseDto,
} from '../types/payment.types';
import { PAYMENT_POLL_INTERVAL_MS } from '../utils/payment-format';

vi.mock('@/shared/components/layout/app-header', () => ({
  AppHeader: () => <div data-testid="app-header" />,
}));
vi.mock('@/shared/components/layout/patient-footer', () => ({
  PatientFooter: () => <div data-testid="patient-footer" />,
}));

class FailingPaymentHub implements HubConnectionLike {
  state: HubConnectionState = HubConnectionState.Disconnected;

  async start(): Promise<void> {
    this.state = HubConnectionState.Disconnected;
    throw new Error('SignalR WebSocket unavailable');
  }

  async stop(): Promise<void> {
    this.state = HubConnectionState.Disconnected;
  }

  async invoke<T = unknown>(): Promise<T> {
    throw new Error('SignalR disconnected');
  }

  on(): void {}
  off(): void {}
  onreconnecting(): void {}
  onreconnected(): void {}
  onclose(): void {}
}

const visitCode = 'VIS-202611-0707';

describe('Phase 07 — Optional vs Mandatory CLS Selection, Reconciliation Isolation, Cross-Day Readiness & Polling Fallback', () => {
  beforeEach(async () => {
    localStorage.clear();
    await resetRealtimeClientForTests();
    useAuthStore.getState().setAuth({
      user: {
        id: 7,
        email: 'patient-p07@medflow.vn',
        fullName: 'Nguyễn Văn Bệnh Nhân',
        roles: ['PATIENT'],
        patientCode: 'PAT-0707',
        staffCode: null,
      },
      accessToken: 'jwt-p07',
      refreshToken: 'refresh-p07',
    });
  });

  afterEach(async () => {
    vi.useRealTimers();
    await resetRealtimeClientForTests();
    useAuthStore.getState().clearAuth();
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('displays Bắt buộc vs Tùy chọn badges, supports multi-charge grouped QR (visitChargeIds) & single-charge QR, and declines unpaid optional CLS while mandatory CLS has no decline button', async () => {
    const user = userEvent.setup();

    let currentCharges: VisitChargesResponseDto = {
      visitId: 707,
      visitCode,
      visitStatus: 'AWAITING_RESULTS',
      patientId: 7,
      payableTotal: 450_000,
      selectedAmountDue: 200_000,
      hasUnpricedPayableCharges: false,
      amountDue: 300_000,
      currency: 'VND',
      recognizedPaidTotal: 150_000,
      settlementStatus: 'UNPAID',
      paymentId: 10,
      paymentCode: 'MF10',
      paymentStatus: 'PAID',
      paymentAmount: 150_000,
      paymentMethod: 'VIETQR',
      paidAt: '2026-11-26T08:30:00',
      externalReference: 'FT-EX-10',
      canPayByQr: true,
      charges: [
        {
          chargeId: 7001,
          chargeSource: 'PERSISTED',
          serviceId: 1,
          serviceCode: 'EX-01',
          serviceName: 'Khám Nội tiêu hóa',
          diagnosticOrderId: null,
          unitPrice: 150_000,
          paymentRequirement: 'REQUIRED',
          payable: true,
          chargeDataStatus: 'VALID',
          chargeType: 'EXAMINATION',
          chargeStatus: 'ACTIVE',
          lineSettlementStatus: 'PAID',
          canPayByQr: false,
          canDecline: false,
        },
        {
          chargeId: 7002,
          chargeSource: 'PERSISTED',
          serviceId: 2,
          serviceCode: 'XN-CTM',
          serviceName: 'Xét nghiệm công thức máu',
          diagnosticOrderId: 8801,
          diagnosticOrderCode: 'DIA-202611-8801',
          unitPrice: 200_000,
          paymentRequirement: 'REQUIRED',
          payable: true,
          chargeDataStatus: 'VALID',
          chargeType: 'DIAGNOSTIC',
          chargeStatus: 'ACTIVE',
          lineSettlementStatus: 'UNPAID',
          canPayByQr: true,
          requiredForConclusion: true,
          selectionStatus: 'ACCEPTED',
          canDecline: false,
        },
        {
          chargeId: 7003,
          chargeSource: 'PERSISTED',
          serviceId: 3,
          serviceCode: 'SA-OB',
          serviceName: 'Siêu âm ổ bụng tầm soát',
          diagnosticOrderId: 8802,
          diagnosticOrderCode: 'DIA-202611-8802',
          unitPrice: 100_000,
          paymentRequirement: 'REQUIRED',
          payable: true,
          chargeDataStatus: 'VALID',
          chargeType: 'DIAGNOSTIC',
          chargeStatus: 'ACTIVE',
          lineSettlementStatus: 'UNPAID',
          canPayByQr: true,
          requiredForConclusion: false,
          selectionStatus: 'UNDECIDED',
          canDecline: true,
        },
      ],
    };

    const getSpy = vi.spyOn(apiClient, 'get').mockImplementation(async () => ({
      data: currentCharges,
    }));

    const postSpy = vi
      .spyOn(apiClient, 'post')
      .mockImplementation(async (url: string, body?: unknown) => {
        if (url.endsWith('/diagnostic-orders/selection')) {
          currentCharges = {
            ...currentCharges,
            selectedAmountDue: 300_000,
            charges: currentCharges.charges.map((c) =>
              c.chargeId === 7003 ? { ...c, selectionStatus: 'ACCEPTED' } : c,
            ),
          };
          return { data: currentCharges };
        }

        if (url.endsWith('/payments/qr')) {
          const payload = (body ?? {}) as { visitChargeId?: number; visitChargeIds?: number[] };
          if (payload.visitChargeIds && payload.visitChargeIds.length === 2) {
            const groupedQr: QrPaymentResponseDto = {
              visitId: 707,
              visitCode,
              paymentId: 501,
              paymentCode: 'MF501',
              amount: 300_000,
              currency: 'VND',
              status: 'PENDING',
              qrPayload: '0002010102123854-GRP',
              bankBin: '970422',
              accountNumber: '0123456789',
              accountName: 'BENH VIEN P07',
              transferContent: 'MF501',
              issueKind: 'CREATED',
              allocations: [
                {
                  visitChargeId: 7002,
                  serviceName: 'Xét nghiệm công thức máu',
                  allocatedAmount: 200_000,
                  status: 'PENDING',
                },
                {
                  visitChargeId: 7003,
                  serviceName: 'Siêu âm ổ bụng tầm soát',
                  allocatedAmount: 100_000,
                  status: 'PENDING',
                },
              ],
            };
            return { data: groupedQr };
          }

          const singleQr: QrPaymentResponseDto = {
            visitId: 707,
            visitCode,
            paymentId: 502,
            paymentCode: 'MF502',
            amount: 200_000,
            currency: 'VND',
            status: 'PENDING',
            qrPayload: '0002010102123854-SNG',
            bankBin: '970422',
            accountNumber: '0123456789',
            accountName: 'BENH VIEN P07',
            transferContent: 'MF502',
            issueKind: 'CREATED',
            visitChargeId: payload.visitChargeId ?? 7002,
          };
          return { data: singleQr };
        }

        currentCharges = {
          ...currentCharges,
          payableTotal: 350_000,
          selectedAmountDue: 200_000,
          amountDue: 200_000,
          charges: currentCharges.charges.map((c) =>
            c.chargeId === 7003
              ? {
                  ...c,
                  payable: false,
                  chargeStatus: 'CANCELLED',
                  lineSettlementStatus: 'CANCELLED',
                  selectionStatus: 'DECLINED',
                  canPayByQr: false,
                  canDecline: false,
                }
              : c,
          ),
        };
        const declineResponse: DeclineDiagnosticOrderResponse = {
          visitId: 707,
          visitCode,
          visitStatus: 'AWAITING_RESULTS',
          orderId: 8802,
          orderCode: 'DIA-202611-8802',
          orderStatus: 'CANCELLED',
          selectionStatus: 'DECLINED',
          visitChargeId: 7003,
          chargeStatus: 'CANCELLED',
          wasAlreadyPaid: false,
          cancelledPendingQrCount: 0,
          transitionedToReturnConsultation: false,
          chargesSnapshot: currentCharges,
        };
        return { data: declineResponse };
      });

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/payments/visits/${visitCode}`]}>
          <Routes>
            <Route path="/payments/visits/:visitIdOrCode" element={<VisitPaymentPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByText('300.000 VNĐ')).toBeInTheDocument();
    expect(screen.getByText('Bắt buộc')).toBeInTheDocument();
    expect(screen.getByText('Tùy chọn')).toBeInTheDocument();
    expect(screen.getByText('Chưa quyết định')).toBeInTheDocument();

    // Mandatory line has NO decline button; optional line has the single "Không thực hiện" button
    const declineButtons = screen.getAllByRole('button', { name: /Không thực hiện/i });
    expect(declineButtons).toHaveLength(1);

    const mandatoryRow = screen
      .getByText('Xét nghiệm công thức máu')
      .closest('div.p-4') as HTMLElement;
    expect(
      within(mandatoryRow).queryByRole('button', { name: /Không thực hiện/i }),
    ).not.toBeInTheDocument();

    // Check optional CLS -> calls /selection -> enables grouped QR for [7002, 7003]
    const optionalCheckbox = screen.getByRole('checkbox', {
      name: 'Chọn thanh toán Siêu âm ổ bụng tầm soát',
    });
    expect(optionalCheckbox).not.toBeChecked();
    await user.click(optionalCheckbox);

    await waitFor(() =>
      expect(postSpy).toHaveBeenCalledWith(
        `/patients/visits/${visitCode}/diagnostic-orders/selection`,
        { acceptedOrderIds: [8802] },
      ),
    );

    const groupedBtn = await screen.findByRole('button', {
      name: /Thanh toán các dịch vụ đã chọn \(2\)/i,
    });
    await user.click(groupedBtn);

    expect(postSpy).toHaveBeenCalledWith(`/patients/visits/${visitCode}/payments/qr`, {
      visitChargeIds: [7002, 7003],
      idempotencyKey: expect.stringMatching(/^qr-/),
    });
    expect(await screen.findByRole('img', { name: 'Mã QR thanh toán MF501' })).toBeInTheDocument();

    // Return to charge list ("Chọn khoản phí khác") and decline unpaid optional CLS
    await user.click(screen.getByRole('button', { name: 'Chọn khoản phí khác' }));

    await user.click(await screen.findByRole('button', { name: /Không thực hiện/i }));
    const reasonInput = screen.getByLabelText('Lý do không thực hiện dịch vụ tùy chọn');
    await user.type(reasonInput, 'Chưa cần siêu âm tầm soát hôm nay');
    await user.click(screen.getByRole('button', { name: 'Xác nhận không thực hiện' }));

    await waitFor(() =>
      expect(postSpy).toHaveBeenCalledWith(
        `/patients/visits/${visitCode}/diagnostic-orders/8802/decline`,
        { reason: 'Chưa cần siêu âm tầm soát hôm nay' },
      ),
    );

    expect(await screen.findByText('Đã hủy')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Không thực hiện/i })).not.toBeInTheDocument();

    // Single remaining active mandatory charge uses single-charge QR ({ visitChargeId: 7002 })
    const singleQrBtn = screen.getByRole('button', { name: /Tạo mã VietQR/i });
    await user.click(singleQrBtn);
    expect(postSpy).toHaveBeenCalledWith(`/patients/visits/${visitCode}/payments/qr`, {
      visitChargeId: 7002,
      idempotencyKey: expect.stringMatching(/^qr-/),
    });
    expect(await screen.findByRole('img', { name: 'Mã QR thanh toán MF502' })).toBeInTheDocument();
    expect(getSpy.mock.calls.length).toBeGreaterThanOrEqual(2);
  });

  it('renders PAID_NOT_PERFORMED (SKIPPED_AFTER_PAID — zero pay/refund buttons) and isolates hasReconciliationItems / CANCELLED_REFUND_PENDING so remaining ACTIVE UNPAID charges stay payable', async () => {
    const user = userEvent.setup();

    const reconciliationCharges: VisitChargesResponseDto = {
      visitId: 708,
      visitCode,
      visitStatus: 'AWAITING_RESULTS',
      patientId: 7,
      payableTotal: 350_000,
      hasUnpricedPayableCharges: false,
      amountDue: 200_000,
      currency: 'VND',
      recognizedPaidTotal: 250_000,
      settlementStatus: 'UNPAID',
      paymentId: 21,
      paymentCode: 'MF21',
      paymentStatus: 'PAID',
      paymentAmount: 100_000,
      paymentMethod: 'VIETQR',
      paidAt: '2026-11-26T08:45:00',
      externalReference: 'FT-OPT-21',
      canPayByQr: true,
      hasReconciliationItems: true,
      charges: [
        {
          chargeId: 7101,
          chargeSource: 'PERSISTED',
          serviceId: 1,
          serviceCode: 'EX-01',
          serviceName: 'Khám Nội tiêu hóa',
          diagnosticOrderId: null,
          unitPrice: 150_000,
          paymentRequirement: 'REQUIRED',
          payable: true,
          chargeDataStatus: 'VALID',
          chargeType: 'EXAMINATION',
          chargeStatus: 'ACTIVE',
          lineSettlementStatus: 'PAID',
          canPayByQr: false,
        },
        {
          chargeId: 7102,
          chargeSource: 'PERSISTED',
          serviceId: 2,
          serviceCode: 'XN-CTM',
          serviceName: 'Xét nghiệm công thức máu',
          diagnosticOrderId: 8901,
          diagnosticOrderCode: 'DIA-202611-8901',
          unitPrice: 200_000,
          paymentRequirement: 'REQUIRED',
          payable: true,
          chargeDataStatus: 'VALID',
          chargeType: 'DIAGNOSTIC',
          chargeStatus: 'ACTIVE',
          lineSettlementStatus: 'UNPAID',
          canPayByQr: true,
          requiredForConclusion: true,
          selectionStatus: 'ACCEPTED',
          canDecline: false,
        },
        {
          chargeId: 7103,
          chargeSource: 'PERSISTED',
          serviceId: 3,
          serviceCode: 'XQ-NGUC',
          serviceName: 'X-quang ngực tùy chọn bỏ sau thanh toán',
          diagnosticOrderId: 8902,
          diagnosticOrderCode: 'DIA-202611-8902',
          unitPrice: 100_000,
          paymentRequirement: 'REQUIRED',
          payable: false,
          chargeDataStatus: 'VALID',
          chargeType: 'DIAGNOSTIC',
          chargeStatus: 'PAID_NOT_PERFORMED',
          lineSettlementStatus: 'PAID_NOT_PERFORMED',
          selectionStatus: 'SKIPPED_AFTER_PAID',
          paymentId: 21,
          paymentCode: 'MF21',
          paymentStatus: 'PAID',
          canPayByQr: false,
          requiredForConclusion: false,
          canDecline: false,
        },
        {
          chargeId: 7104,
          chargeSource: 'PERSISTED',
          serviceId: 4,
          serviceCode: 'SA-OB',
          serviceName: 'Siêu âm ổ bụng chuyển khoản muộn',
          diagnosticOrderId: 8903,
          diagnosticOrderCode: 'DIA-202611-8903',
          unitPrice: 100_000,
          paymentRequirement: 'REQUIRED',
          payable: false,
          chargeDataStatus: 'VALID',
          chargeType: 'DIAGNOSTIC',
          chargeStatus: 'CANCELLED_REFUND_PENDING',
          paymentId: 22,
          paymentCode: 'MF22',
          paymentStatus: 'MISMATCH',
          canPayByQr: false,
          requiredForConclusion: false,
          selectionStatus: 'DECLINED',
          canDecline: false,
        },
      ],
    };

    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: reconciliationCharges });

    const qrResp: QrPaymentResponseDto = {
      visitId: 708,
      visitCode,
      paymentId: 99,
      paymentCode: 'MF99',
      amount: 200_000,
      currency: 'VND',
      status: 'PENDING',
      qrPayload: '0002010102123854',
      bankBin: '970422',
      accountNumber: '0123456789',
      accountName: 'BENH VIEN P07',
      transferContent: 'MF99',
      issueKind: 'CREATED',
      visitChargeId: 7102,
    };
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: qrResp });

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/payments/visits/${visitCode}`]}>
          <Routes>
            <Route path="/payments/visits/:visitIdOrCode" element={<VisitPaymentPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByText('Đã thanh toán — Không sử dụng')).toBeInTheDocument();
    expect(screen.getByText('Chờ đối soát')).toBeInTheDocument();
    expect(
      screen.getByText(/Các dịch vụ hợp lệ khác bên dưới vẫn có thể thanh toán bình thường/i),
    ).toBeInTheDocument();

    // PAID_NOT_PERFORMED row has zero pay button, zero checkbox, and zero refund button (no refund module!)
    const paidNotPerformedRow = screen
      .getByText('X-quang ngực tùy chọn bỏ sau thanh toán')
      .closest('div.p-4') as HTMLElement;
    expect(within(paidNotPerformedRow).queryByRole('checkbox')).not.toBeInTheDocument();
    expect(within(paidNotPerformedRow).queryByRole('button')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /hoàn tiền|refund/i })).not.toBeInTheDocument();

    // Must NOT block remaining active unpaid charges with full-page mismatch screen!
    expect(screen.queryByText('Cần đối soát thanh toán tại quầy')).not.toBeInTheDocument();

    const qrBtn = screen.getByRole('button', { name: /Tạo mã VietQR/i });
    expect(qrBtn).toBeEnabled();
    await user.click(qrBtn);

    expect(postSpy).toHaveBeenCalledWith(`/patients/visits/${visitCode}/payments/qr`, {
      visitChargeId: 7102,
      idempotencyKey: expect.stringMatching(/^qr-/),
    });
    expect(await screen.findByRole('img', { name: 'Mã QR thanh toán MF99' })).toBeInTheDocument();
  });

  it('displays cross-day readyForReturnAt && !isPresentToday Reception check-in/resume notice without claiming patient is actively being called', async () => {
    const crossDayAbsentCharges: VisitChargesResponseDto = {
      visitId: 710,
      visitCode: 'VIS-202611-0710',
      visitStatus: 'AWAITING_RESULTS',
      patientId: 7,
      payableTotal: 350_000,
      hasUnpricedPayableCharges: false,
      amountDue: 0,
      currency: 'VND',
      recognizedPaidTotal: 350_000,
      settlementStatus: 'PAID',
      paymentId: 31,
      paymentCode: 'MF31',
      paymentStatus: 'PAID',
      paymentAmount: 350_000,
      paymentMethod: 'VIETQR',
      paidAt: '2026-11-25T09:15:00',
      externalReference: 'FT-CD-31',
      canPayByQr: false,
      readyForReturnAt: '2026-11-26T10:30:00',
      isPresentToday: false,
      presenceState: 'LEFT',
      charges: [],
    };

    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: crossDayAbsentCharges });

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/payments/visits/VIS-202611-0710']}>
          <Routes>
            <Route path="/payments/visits/:visitIdOrCode" element={<VisitPaymentPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(
      await screen.findByText(
        /Đủ điều kiện quay lại khám kết luận — vui lòng đến quầy Tiếp đón xác nhận có mặt/i,
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/đang gọi vào phòng|mời vào phòng khám ngay/i),
    ).not.toBeInTheDocument();
  });

  it('hydrates activePendingQr banner, falls back to HTTP polling when SignalR is disconnected, and stops polling once settled (PAID)', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    setRealtimeConnectionFactoryForTests(() => new FailingPaymentHub());

    let serverCharges: VisitChargesResponseDto = {
      visitId: 709,
      visitCode,
      visitStatus: 'AWAITING_RESULTS',
      patientId: 7,
      payableTotal: 300_000,
      hasUnpricedPayableCharges: false,
      amountDue: 300_000,
      currency: 'VND',
      recognizedPaidTotal: 0,
      settlementStatus: 'UNPAID',
      paymentId: 88,
      paymentCode: 'MF88',
      paymentStatus: 'PENDING',
      paymentAmount: 300_000,
      paymentMethod: 'VIETQR',
      paidAt: null,
      externalReference: null,
      canPayByQr: true,
      activePendingQrPaymentId: 88,
      activePendingQrPaymentCode: 'MF88',
      activePendingQrAmount: 300_000,
      activePendingQrChargeIds: [7201, 7202],
      charges: [
        {
          chargeId: 7201,
          chargeSource: 'PERSISTED',
          serviceId: 1,
          serviceCode: 'EX-01',
          serviceName: 'Khám Nội',
          diagnosticOrderId: null,
          unitPrice: 150_000,
          paymentRequirement: 'REQUIRED',
          payable: true,
          chargeDataStatus: 'VALID',
          chargeType: 'EXAMINATION',
          chargeStatus: 'ACTIVE',
          lineSettlementStatus: 'UNPAID',
          paymentStatus: 'PENDING',
          canPayByQr: true,
        },
        {
          chargeId: 7202,
          chargeSource: 'PERSISTED',
          serviceId: 2,
          serviceCode: 'XN-01',
          serviceName: 'Xét nghiệm máu',
          diagnosticOrderId: 9901,
          unitPrice: 150_000,
          paymentRequirement: 'REQUIRED',
          payable: true,
          chargeDataStatus: 'VALID',
          chargeType: 'DIAGNOSTIC',
          chargeStatus: 'ACTIVE',
          lineSettlementStatus: 'UNPAID',
          paymentStatus: 'PENDING',
          canPayByQr: true,
          requiredForConclusion: true,
          selectionStatus: 'ACCEPTED',
        },
      ],
    };

    const getSpy = vi.spyOn(apiClient, 'get').mockImplementation(async () => ({
      data: serverCharges,
    }));

    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        visitId: 709,
        visitCode,
        paymentId: 88,
        paymentCode: 'MF88',
        amount: 300_000,
        currency: 'VND',
        status: 'PENDING',
        qrPayload: '0002010102123854-MF88',
        bankBin: '970422',
        accountNumber: '0123456789',
        accountName: 'BENH VIEN P07',
        transferContent: 'MF88',
        issueKind: 'REUSED',
        allocations: [
          {
            visitChargeId: 7201,
            serviceName: 'Khám Nội',
            allocatedAmount: 150_000,
            status: 'PENDING',
          },
          {
            visitChargeId: 7202,
            serviceName: 'Xét nghiệm máu',
            allocatedAmount: 150_000,
            status: 'PENDING',
          },
        ],
      } satisfies QrPaymentResponseDto,
    });

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/payments/visits/${visitCode}`]}>
          <Routes>
            <Route path="/payments/visits/:visitIdOrCode" element={<VisitPaymentPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    // Pending QR hydration banner & reopen action
    expect(await screen.findByText('MF88')).toBeInTheDocument();
    const reopenBtn = screen.getByRole('button', { name: 'Mở lại mã QR đang chờ' });
    await user.click(reopenBtn);
    expect(postSpy).toHaveBeenCalledWith(`/patients/visits/${visitCode}/payments/qr`, {
      visitChargeIds: [7201, 7202],
      idempotencyKey: expect.stringMatching(/^qr-/),
    });
    expect(await screen.findByRole('img', { name: 'Mã QR thanh toán MF88' })).toBeInTheDocument();

    const callsBeforePoll = getSpy.mock.calls.length;

    // Advance one disconnected poll interval while still PENDING -> polls REST
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PAYMENT_POLL_INTERVAL_MS + 100);
    });
    expect(getSpy.mock.calls.length).toBeGreaterThan(callsBeforePoll);

    // Backend settles payment while SignalR remains completely unavailable
    serverCharges = {
      ...serverCharges,
      settlementStatus: 'PAID',
      paymentStatus: 'PAID',
      recognizedPaidTotal: 300_000,
      amountDue: 0,
      paidAt: '2026-11-26T09:10:00',
      externalReference: 'FT-POLL-709',
      activePendingQrPaymentId: null,
      activePendingQrPaymentCode: null,
      activePendingQrAmount: null,
      activePendingQrChargeIds: null,
      charges: serverCharges.charges.map((c) => ({
        ...c,
        lineSettlementStatus: 'PAID',
        paymentStatus: 'PAID',
        canPayByQr: false,
      })),
    };

    await act(async () => {
      await vi.advanceTimersByTimeAsync(PAYMENT_POLL_INTERVAL_MS + 100);
    });

    expect(await screen.findByText('Đã thanh toán')).toBeInTheDocument();
    expect(screen.getByText('FT-POLL-709')).toBeInTheDocument();

    // Once settled (PAID), refetchInterval returns false -> zero further HTTP polls!
    const callsAtSettled = getSpy.mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PAYMENT_POLL_INTERVAL_MS * 4);
    });
    expect(getSpy.mock.calls.length).toBe(callsAtSettled);
  });

  it('isolates query keys across visit switches (visitCode A -> visitCode B) so a late stale response from visit A never leaks onto visit B', async () => {
    const user = userEvent.setup();

    let resolveLateVisitA!: (value: { data: VisitChargesResponseDto }) => void;
    const lateVisitAPromise = new Promise<{ data: VisitChargesResponseDto }>((resolve) => {
      resolveLateVisitA = resolve;
    });

    const visitBCharges: VisitChargesResponseDto = {
      visitId: 802,
      visitCode: 'VIS-B-0802',
      visitStatus: 'CHECKED_IN',
      patientId: 7,
      payableTotal: 220_000,
      hasUnpricedPayableCharges: false,
      amountDue: 220_000,
      currency: 'VND',
      recognizedPaidTotal: 0,
      settlementStatus: 'UNPAID',
      paymentId: null,
      paymentCode: null,
      paymentStatus: 'UNPAID',
      paymentAmount: null,
      paymentMethod: 'VIETQR',
      paidAt: null,
      externalReference: null,
      canPayByQr: true,
      charges: [],
    };

    vi.spyOn(apiClient, 'get').mockImplementation((url: string) => {
      if (url.includes('VIS-A-0801')) {
        return lateVisitAPromise as ReturnType<typeof apiClient.get>;
      }
      return Promise.resolve({ data: visitBCharges }) as ReturnType<typeof apiClient.get>;
    });

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/payments/visits/VIS-A-0801']}>
          <Link to="/payments/visits/VIS-B-0802">Chuyển sang lượt khám B</Link>
          <Routes>
            <Route path="/payments/visits/:visitIdOrCode" element={<VisitPaymentPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    // Switch immediately to Visit B while Visit A's request is still in flight
    await user.click(screen.getByRole('link', { name: 'Chuyển sang lượt khám B' }));

    expect(await screen.findByText('220.000 VNĐ')).toBeInTheDocument();
    expect(screen.getAllByText('#VIS-B-0802').length).toBeGreaterThan(0);

    // Now Visit A's slow request resolves as PAID -> must NOT overwrite Visit B's UNPAID UI!
    await act(async () => {
      resolveLateVisitA({
        data: {
          ...visitBCharges,
          visitId: 801,
          visitCode: 'VIS-A-0801',
          settlementStatus: 'PAID',
          paymentStatus: 'PAID',
          recognizedPaidTotal: 150_000,
          amountDue: 0,
          externalReference: 'FT-STALE-A',
          canPayByQr: false,
        },
      });
    });

    expect(screen.queryByText('Đã thanh toán')).not.toBeInTheDocument();
    expect(screen.queryByText('FT-STALE-A')).not.toBeInTheDocument();
    expect(screen.getByText('220.000 VNĐ')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Tạo mã QR thanh toán/i })).toBeEnabled();
  });
});
