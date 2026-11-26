import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type {
  GenerateQrPaymentResponse,
  VisitChargesResponse,
} from '@/features/payment/types/payment.types';
import { apiClient } from '@/shared/lib/axios';

import { ConsultationVietQrModal } from '../components/consultation-vietqr-modal';

const visitCode = 'VIS-202611-0088';

const mockCharges: VisitChargesResponse = {
  visitId: 88,
  visitCode,
  visitStatus: 'IN_PROGRESS',
  patientId: 12,
  payableTotal: 450000,
  hasUnpricedPayableCharges: false,
  amountDue: 450000,
  selectedAmountDue: 270000,
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
  charges: [
    {
      chargeId: 301,
      chargeSource: 'PERSISTED',
      serviceId: 10,
      serviceCode: 'XN-CTM',
      serviceName: 'Công thức máu bắt buộc',
      diagnosticOrderId: 501,
      unitPrice: 120000,
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
      chargeId: 302,
      chargeSource: 'PERSISTED',
      serviceId: 11,
      serviceCode: 'SH-GAN',
      serviceName: 'Sinh hóa gan bắt buộc',
      diagnosticOrderId: 502,
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
      chargeId: 303,
      chargeSource: 'PERSISTED',
      serviceId: 12,
      serviceCode: 'SA-BUNG',
      serviceName: 'Siêu âm bụng tùy chọn',
      diagnosticOrderId: 503,
      unitPrice: 180000,
      paymentRequirement: 'REQUIRED',
      payable: true,
      chargeDataStatus: 'VALID',
      chargeType: 'DIAGNOSTIC',
      chargeStatus: 'ACTIVE',
      lineSettlementStatus: 'UNPAID',
      canPayByQr: true,
      requiredForConclusion: false,
      selectionStatus: 'UNDECIDED',
    },
  ],
};

afterEach(() => {
  vi.restoreAllMocks();
});

describe('ConsultationVietQrModal', () => {
  it('displays grouped VietQR for selected charges and never exposes a manual PAID button', async () => {
    const user = userEvent.setup();
    const get = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: mockCharges });
    const qrPayloadResponse: GenerateQrPaymentResponse = {
      visitId: 88,
      visitCode,
      paymentId: 901,
      paymentCode: 'MF901',
      amount: 270000,
      currency: 'VND',
      status: 'PENDING',
      qrPayload: '0002010102123854',
      bankBin: '970422',
      accountNumber: '0123456789',
      accountName: 'BENH VIEN DEMO',
      transferContent: 'MF901',
      issueKind: 'CREATED',
      allocations: [
        {
          visitChargeId: 301,
          serviceCode: 'XN-CTM',
          serviceName: 'Công thức máu bắt buộc',
          allocatedAmount: 120000,
          status: 'PENDING',
        },
        {
          visitChargeId: 302,
          serviceCode: 'SH-GAN',
          serviceName: 'Sinh hóa gan bắt buộc',
          allocatedAmount: 150000,
          status: 'PENDING',
        },
      ],
    };
    const post = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: qrPayloadResponse });

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <ConsultationVietQrModal
          open
          visitIdOrCode={visitCode}
          patientName="Nguyễn Thị Mai"
          onClose={vi.fn()}
        />
      </QueryClientProvider>,
    );

    expect(await screen.findByText('Công thức máu bắt buộc')).toBeInTheDocument();
    expect(get).toHaveBeenCalledWith(`/doctor/visits/${visitCode}/charges`);
    expect(
      screen.queryByRole('button', { name: /Xác nhận đã thu|Xác nhận đã thanh toán/i }),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Tạo mã VietQR \(2\)/i }));

    expect(post).toHaveBeenCalledWith(`/doctor/visits/${visitCode}/payments/qr`, {
      visitChargeIds: [301, 302],
    });
    expect(await screen.findByRole('img', { name: 'Mã QR thanh toán MF901' })).toBeInTheDocument();
  });
});
