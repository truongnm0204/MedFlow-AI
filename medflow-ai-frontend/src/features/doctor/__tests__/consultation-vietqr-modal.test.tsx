import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type {
  GenerateQrPaymentResponse,
  VisitChargesResponse,
} from '@/features/payment/types/payment.types';
import { apiClient } from '@/shared/lib/axios';

import { ConsultationCompleteVisitPanel } from '../components/consultation-complete-visit-panel';
import { ConsultationDiagnosticOrderPanel } from '../components/consultation-diagnostic-order-panel';
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
      idempotencyKey: expect.any(String),
    });
    expect(await screen.findByRole('img', { name: 'Mã QR thanh toán MF901' })).toBeInTheDocument();
  });

  it('hides QR image on CLOSED/PAID responses and rotates idempotencyKey when generating a fresh QR', async () => {
    const user = userEvent.setup();
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: mockCharges });
    const post = vi
      .spyOn(apiClient, 'post')
      .mockResolvedValueOnce({
        data: {
          visitId: 88,
          visitCode,
          paymentId: 902,
          paymentCode: 'MF902',
          amount: 270000,
          currency: 'VND',
          status: 'EXPIRED',
          qrPayload: '',
          bankBin: '970422',
          accountNumber: '0123456789',
          accountName: 'BENH VIEN DEMO',
          transferContent: 'MF902',
          issueKind: 'REUSED',
        } satisfies GenerateQrPaymentResponse,
      })
      .mockResolvedValueOnce({
        data: {
          visitId: 88,
          visitCode,
          paymentId: 903,
          paymentCode: 'MF903',
          amount: 270000,
          currency: 'VND',
          status: 'PENDING',
          qrPayload: '0002010102123854-NEW',
          bankBin: '970422',
          accountNumber: '0123456789',
          accountName: 'BENH VIEN DEMO',
          transferContent: 'MF903',
          issueKind: 'CREATED',
        } satisfies GenerateQrPaymentResponse,
      });

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

    await user.click(await screen.findByRole('button', { name: /Tạo mã VietQR \(2\)/i }));

    expect(await screen.findByText('Mã QR không còn hiệu lực quét')).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: /Mã QR thanh toán/i })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Tạo mã VietQR mới/i }));

    expect(await screen.findByRole('img', { name: 'Mã QR thanh toán MF903' })).toBeInTheDocument();
    const key1 = (post.mock.calls[0]?.[1] as { idempotencyKey?: string })?.idempotencyKey;
    const key2 = (post.mock.calls[1]?.[1] as { idempotencyKey?: string })?.idempotencyKey;
    expect(key1).toBeTruthy();
    expect(key2).toBeTruthy();
    expect(key2).not.toBe(key1);
  });

  it('supports Doctor expired-order revalidation and overdue-return clinical snapshot approval', async () => {
    const user = userEvent.setup();
    const onRevalidate = vi.fn().mockResolvedValue(true);
    const onApproveOverdue = vi.fn().mockResolvedValue(true);

    render(
      <div>
        <ConsultationDiagnosticOrderPanel
          existingOrders={[
            {
              orderId: 777,
              orderCode: 'DIA-777',
              visitId: 88,
              visitCode,
              orderingDoctorId: 11,
              orderingDoctorCode: 'DOC-11',
              orderingDoctorName: 'BS. Khôi',
              serviceId: 5,
              serviceCode: 'DIA-CBC',
              serviceName: 'Công thức máu',
              category: 'LABORATORY',
              unitPrice: 120000,
              requiresResultVerification: true,
              queueEntryId: 500,
              formattedQueueEntryId: 'q-500',
              ticketNo: 'CLS-001',
              queueNumber: 1,
              queueStatus: 'DEFERRED',
              servicePointId: 3,
              servicePointName: 'Phòng Xét nghiệm',
              roomId: 5,
              roomCode: 'RM-005',
              roomName: 'Phòng CLS 205',
              priority: 'NORMAL',
              status: 'CONFIRMED',
              requiredForConclusion: true,
              instruction: 'Nhịn ăn sáng',
              orderedAt: '2026-11-10T08:00:00',
              confirmedAt: '2026-11-10T08:05:00',
              isResultFinalized: false,
              selectionStatus: 'ACCEPTED',
              paymentStatus: 'PAID',
              isExpiredForReview: true,
            },
          ]}
          catalogServices={[]}
          canCreateOrder
          hasOrderPermission
          expectedStateVersion={3}
          onRevalidateOrder={onRevalidate}
        />

        <ConsultationCompleteVisitPanel
          workspaceState="IN_PROGRESS"
          visit={{
            visitId: 88,
            visitCode,
            status: 'IN_PROGRESS',
            statusLabel: 'Đang khám',
            visitType: 'WALK_IN',
            visitTypeLabel: 'Khám vãng lai',
            reasonForVisit: 'Đau đầu',
            appointmentId: null,
            appointmentCode: null,
            scheduledStartAt: null,
            scheduledEndAt: null,
            checkedInAt: '2026-11-10T08:00:00',
            startedAt: '2026-11-26T09:00:00',
            completedAt: null,
            hospitalId: 1,
            hospitalCode: 'H-01',
            hospitalName: 'BV MedFlow',
            departmentId: 1,
            departmentCode: 'NOI',
            departmentName: 'Khoa Nội',
            specialtyId: 1,
            specialtyCode: 'NOI',
            specialtyName: 'Nội khoa',
            roomId: 1,
            roomCode: 'P.101',
            roomName: 'Phòng 101',
            roomLocation: 'Tầng 1',
            assignedDoctorId: 11,
            assignedDoctorCode: 'DOC-11',
            assignedDoctorName: 'BS. Khôi',
            assignedDoctorTitle: 'BS',
            serviceId: 1,
            serviceCode: 'KHAM-01',
            serviceName: 'Khám Nội',
            isCrossDayVisit: true,
            isOverdueForReview: true,
            isReturnOverdue: true,
            isOverdueReturnApproved: false,
          }}
          clinicalContext={{
            medicalRecordId: 10,
            medicalRecordStatus: 'DRAFT',
            finalizedAt: null,
            currentRevisionNo: 2,
            isConfirmed: true,
            lastRecordedAt: '2026-11-26T09:05:00',
            presentingComplaint: 'Đau đầu',
            symptomHistory: '3 ngày',
            relevantHistoryAllergies: null,
            vitalSigns: {
              bpSystolicMmhg: 120,
              bpDiastolicMmhg: 80,
              bloodPressureLabel: '120/80 mmHg',
              pulseBpm: 72,
              temperatureC: 36.7,
              spo2Percent: 98,
              heightCm: 170,
              weightKg: 65,
              bmi: 22.5,
            },
            clinicalFindings: 'Tỉnh',
            clinicalConclusion: 'Theo dõi đau đầu căng thẳng',
            amendmentReason: null,
            diagnoses: [
              {
                id: 1,
                icd10CodeId: 100,
                code: 'G44.2',
                name: 'Đau đầu căng thẳng',
                isPrimary: true,
                clinicalDiagnosis: 'Đau đầu căng thẳng',
                status: 'CONFIRMED',
                confirmedAt: '2026-11-26T09:05:00',
                recordedAt: '2026-11-26T09:05:00',
              },
            ],
            diagnosticSummary: {
              totalQueueEntries: 1,
              openQueueEntries: 0,
              completedQueueEntries: 1,
              allMandatoryResultsReady: true,
              finalResultRequired: true,
            },
            prescriptionSummary: {
              totalPrescriptions: 0,
              latestPrescriptionCode: null,
              latestPrescriptionStatus: null,
              latestIssuedAt: null,
              itemCount: 0,
            },
          }}
          canCompleteVisit
          hasWritePermission
          expectedStateVersion={4}
          onApproveOverdueResults={onApproveOverdue}
        />
      </div>,
    );

    await user.click(screen.getByRole('button', { name: /Gia hạn hiệu lực chỉ định/i }));
    await user.type(
      screen.getByLabelText('Lý do gia hạn chỉ định DIA-777'),
      'Tình trạng lâm sàng ổn định, đồng ý gia hạn thực hiện xét nghiệm máu',
    );
    await user.click(screen.getByRole('button', { name: /Xác nhận gia hạn hiệu lực/i }));

    expect(onRevalidate).toHaveBeenCalledWith(777, {
      reason: 'Tình trạng lâm sàng ổn định, đồng ý gia hạn thực hiện xét nghiệm máu',
      expectedStateVersion: 3,
    });

    await user.type(
      screen.getByLabelText('Nhận định chuyên môn xác nhận giá trị kết quả quá hạn'),
      'Kết quả xét nghiệm vẫn còn giá trị chẩn đoán trên lâm sàng hiện tại',
    );
    await user.click(screen.getByRole('button', { name: /Xác nhận giá trị kết quả quá hạn/i }));

    expect(onApproveOverdue).toHaveBeenCalledWith({
      reason: 'Kết quả xét nghiệm vẫn còn giá trị chẩn đoán trên lâm sàng hiện tại',
      expectedStateVersion: 4,
    });
  });
});
