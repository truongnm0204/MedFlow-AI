import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type {
  CollectCashPaymentResponse,
  VisitChargesResponse,
} from '@/features/payment/types/payment.types';
import { apiClient } from '@/shared/lib/axios';

import { ReceptionCashCollectionModal } from '../components/reception-cash-collection-modal';

const visitCode = 'VIS-202611-0099';

const mockCharges: VisitChargesResponse = {
  visitId: 99,
  visitCode,
  visitStatus: 'AWAITING_RESULTS',
  patientId: 15,
  payableTotal: 450000,
  hasUnpricedPayableCharges: false,
  amountDue: 270000,
  selectedAmountDue: 270000,
  currency: 'VND',
  recognizedPaidTotal: 180000,
  settlementStatus: 'UNPAID',
  paymentId: null,
  paymentCode: null,
  paymentStatus: 'UNPAID',
  paymentAmount: null,
  paymentMethod: 'CASH',
  paidAt: null,
  externalReference: null,
  canPayByQr: true,
  charges: [
    {
      chargeId: 401,
      chargeSource: 'PERSISTED',
      serviceId: 1,
      serviceCode: 'KHAM-01',
      serviceName: 'Khám Nội tổng quát',
      diagnosticOrderId: null,
      unitPrice: 120000,
      paymentRequirement: 'REQUIRED',
      payable: true,
      chargeDataStatus: 'VALID',
      chargeType: 'EXAMINATION',
      chargeStatus: 'ACTIVE',
      lineSettlementStatus: 'UNPAID',
      canPayByQr: true,
      selectionStatus: 'ACCEPTED',
    },
    {
      chargeId: 402,
      chargeSource: 'PERSISTED',
      serviceId: 2,
      serviceCode: 'XN-01',
      serviceName: 'Xét nghiệm đường huyết',
      diagnosticOrderId: 920,
      unitPrice: 150000,
      paymentRequirement: 'REQUIRED',
      payable: true,
      chargeDataStatus: 'VALID',
      chargeType: 'DIAGNOSTIC',
      chargeStatus: 'ACTIVE',
      lineSettlementStatus: 'UNPAID',
      canPayByQr: true,
      requiredForConclusion: true,
      selectionStatus: 'ACCEPTED',
    },
    {
      chargeId: 403,
      chargeSource: 'PERSISTED',
      serviceId: 3,
      serviceCode: 'SA-03',
      serviceName: 'Siêu âm bụng tùy chọn đã đóng tiền',
      diagnosticOrderId: 921,
      unitPrice: 180000,
      paymentRequirement: 'REQUIRED',
      payable: false,
      chargeDataStatus: 'VALID',
      chargeType: 'DIAGNOSTIC',
      chargeStatus: 'ACTIVE',
      lineSettlementStatus: 'PAID',
      canPayByQr: false,
      requiredForConclusion: false,
      selectionStatus: 'ACCEPTED',
    },
  ],
};

afterEach(() => {
  vi.restoreAllMocks();
});

describe('ReceptionCashCollectionModal', () => {
  it('collects multi-charge cash payment with idempotencyKey and renders receipt', async () => {
    const user = userEvent.setup();
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: mockCharges });
    const receiptResponse: CollectCashPaymentResponse = {
      visitId: 99,
      visitCode,
      paymentId: 880,
      paymentCode: 'CASH880',
      amount: 270000,
      currency: 'VND',
      status: 'PAID',
      paymentMethod: 'CASH',
      collectedByStaffId: 4,
      collectedByStaffName: 'Lê Thu Ngân',
      paidAt: '2026-11-26T10:30:00',
      idempotencyKey: 'idem-fixed',
      isIdempotentReplay: false,
      allocations: [
        {
          visitChargeId: 401,
          serviceCode: 'KHAM-01',
          serviceName: 'Khám Nội tổng quát',
          allocatedAmount: 120000,
          status: 'PAID',
        },
        {
          visitChargeId: 402,
          serviceCode: 'XN-01',
          serviceName: 'Xét nghiệm đường huyết',
          allocatedAmount: 150000,
          status: 'PAID',
        },
      ],
    };
    const post = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: receiptResponse });

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <ReceptionCashCollectionModal
          open
          target={{ visitIdOrCode: visitCode, patientName: 'Trần Văn Nam', patientCode: 'BN-0099' }}
          onClose={vi.fn()}
        />
      </QueryClientProvider>,
    );

    expect(await screen.findByText('Khám Nội tổng quát')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Xác nhận thu tiền mặt \(2\)/i }));

    expect(post).toHaveBeenCalledWith(
      `/reception/visits/${visitCode}/payments/cash`,
      expect.objectContaining({
        visitChargeIds: [401, 402],
        idempotencyKey: expect.any(String),
        notes: null,
      }),
    );
    expect(await screen.findByText('Đã thu tiền mặt thành công')).toBeInTheDocument();
    expect(screen.getByText('CASH880')).toBeInTheDocument();
  });

  it('records voluntary abandonment of an already-paid optional diagnostic order without refund', async () => {
    const user = userEvent.setup();
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: mockCharges });
    const post = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: {
        visitId: 99,
        visitCode,
        diagnosticOrderId: 921,
        orderCode: 'DIA-921',
        orderStatus: 'CANCELLED',
        selectionStatus: 'SKIPPED_AFTER_PAID',
        visitChargeId: 403,
        chargeStatus: 'PAID_NOT_PERFORMED',
        visitStatus: 'RETURN_CONSULTATION',
      },
    });

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <ReceptionCashCollectionModal
          open
          target={{ visitIdOrCode: visitCode, patientName: 'Trần Văn Nam' }}
          onClose={vi.fn()}
        />
      </QueryClientProvider>,
    );

    await user.click(await screen.findByRole('button', { name: /Bỏ qua sau TT/i }));
    await user.click(screen.getByRole('button', { name: 'Xác nhận' }));

    expect(post).toHaveBeenCalledWith(
      `/reception/visits/${visitCode}/diagnostic-orders/921/decline`,
      {
        confirmAbandonPaid: true,
        reason: 'Bệnh nhân tự nguyện bỏ qua dịch vụ tùy chọn đã thanh toán tại quầy.',
      },
    );
  });
});
