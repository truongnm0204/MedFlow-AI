import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { DiagnosticWorklistTable } from '@/features/diagnostic/components/diagnostic-worklist-table';
import type { DiagnosticWorklistItemDto } from '@/features/diagnostic/types/diagnostic-worklist.types';
import { ConsultationPatientHeaderCard } from '@/features/doctor/components/consultation-patient-header-card';
import { VisitCard } from '@/features/payment/components/visit-card';
import type { PatientVisitListItemDto } from '@/features/payment/types/payment.types';
import { apiClient } from '@/shared/lib/axios';

import { ReceptionVisitResumeModal } from '../components/reception-visit-resume-modal';
import type {
  DeferVisitOrOrdersResponseDto,
  ResumeCrossDayVisitResponseDto,
  VisitResumeContextResponseDto,
} from '../types/visit-cross-day.types';

const visitCode = 'VIS-202611-0042';

const mockContext: VisitResumeContextResponseDto = {
  visitId: 42,
  visitCode,
  checkedInAt: '2026-11-24T08:15:00',
  visitStatus: 'AWAITING_RESULTS',
  presenceState: 'DEFERRED',
  lastPresentDate: '2026-11-24',
  lastResumedAt: null,
  isPresentToday: false,
  readyForReturnAt: '2026-11-25T16:30:00',
  isReadyForReturn: true,
  assignedDoctorId: 11,
  assignedDoctorName: 'BS.CKII Nguyễn Minh Khôi',
  examinationRoomId: 101,
  isOverdueForReview: true,
  overdueReviewReasons: ['DIAGNOSTIC_ORDER_OVERDUE_REVIEW'],
  isAssignedDoctorActive: false,
  chargesSummary: {
    payableTotal: 320000,
    recognizedPaidTotal: 320000,
    amountDue: 0,
  },
  diagnosticOrders: [
    {
      orderId: 701,
      orderCode: 'DIA-202611-0701',
      serviceName: 'Xét nghiệm công thức máu',
      requiredForConclusion: true,
      selectionStatus: 'ACCEPTED',
      orderStatus: 'CONFIRMED',
      isPaymentSettled: true,
      deferredUntilDate: '2026-11-26',
      validityExpiresAt: '2026-12-01T08:15:00',
      isExpiredForReview: false,
      canResumeToday: true,
    },
  ],
  activeTodayQueues: [],
};

function renderWithProviders(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('Stage C Cross-Day Visit Lifecycle UI', () => {
  it('looks up resume-context and resumes paid cross-day DIAGNOSTIC order with idempotency key', async () => {
    const user = userEvent.setup();
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({ data: mockContext });
    const mockResumeResp: ResumeCrossDayVisitResponseDto = {
      visitId: 42,
      visitCode,
      checkedInAt: mockContext.checkedInAt,
      visitStatus: 'AWAITING_RESULTS',
      presenceState: 'PRESENT',
      lastPresentDate: '2026-11-26',
      lastResumedAt: '2026-11-26T08:05:00',
      target: 'DIAGNOSTIC',
      isIdempotentReplay: false,
      assignedDoctorId: 11,
      assignedDoctorName: mockContext.assignedDoctorName,
      examinationRoomId: 101,
      resumedQueues: [
        {
          queueEntryId: 9001,
          ticketNo: 'CLS-001',
          queueNumber: 1,
          queueDate: '2026-11-26',
          queueType: 'DIAGNOSTIC',
          status: 'WAITING',
          presenceStatus: 'PRESENT',
          servicePointId: 201,
          servicePointName: 'Phòng lấy mẫu xét nghiệm',
          roomCode: 'CLS-201',
        },
      ],
      closedPriorDayQueueCount: 1,
    };
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValue({ data: mockResumeResp });
    const onResumed = vi.fn();

    renderWithProviders(<ReceptionVisitResumeModal open onClose={vi.fn()} onResumed={onResumed} />);

    await user.type(screen.getByLabelText('Mã lượt khám cần tra cứu'), visitCode);
    await user.click(screen.getByRole('button', { name: /Tra cứu lượt khám/i }));

    expect(await screen.findByText('DIA-202611-0701')).toBeInTheDocument();
    expect(screen.getByText(/Hẹn ngày 2026-11-26/i)).toBeInTheDocument();
    expect(getSpy).toHaveBeenCalledWith(`/reception/visits/${visitCode}/resume-context`);

    await user.click(
      screen.getByRole('button', { name: /Xác nhận có mặt & Xếp hàng CLS hôm nay/i }),
    );

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        `/reception/visits/${visitCode}/resume`,
        expect.objectContaining({
          target: 'DIAGNOSTIC',
          diagnosticOrderIds: [701],
          idempotencyKey: expect.any(String),
        }),
      );
    });
    expect(onResumed).toHaveBeenCalledWith(mockResumeResp);
  });

  it('requires doctor reassignment on RETURN_CONSULTATION when original doctor is off-duty, then resumes cleanly', async () => {
    const user = userEvent.setup();
    vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
      if (url.includes('/assignable-doctors')) {
        return {
          data: {
            doctors: [
              {
                doctorId: 22,
                doctorCode: 'DOC-022',
                fullName: 'BS.CKI Trần Thu Hà',
                roomCode: 'P.103',
              },
            ],
          },
        };
      }
      return { data: mockContext };
    });

    const mockReturnResp: ResumeCrossDayVisitResponseDto = {
      visitId: 42,
      visitCode,
      checkedInAt: mockContext.checkedInAt,
      visitStatus: 'RETURN_CONSULTATION',
      presenceState: 'PRESENT',
      lastPresentDate: '2026-11-26',
      lastResumedAt: '2026-11-26T09:10:00',
      target: 'RETURN_CONSULTATION',
      isIdempotentReplay: false,
      assignedDoctorId: 22,
      assignedDoctorName: 'BS.CKI Trần Thu Hà',
      examinationRoomId: 103,
      resumedQueues: [
        {
          queueEntryId: 9002,
          ticketNo: 'P.103-001',
          queueNumber: 1,
          queueDate: '2026-11-26',
          queueType: 'RETURN_CONSULTATION',
          status: 'READY',
          presenceStatus: 'PRESENT',
          servicePointId: 103,
          servicePointName: 'Buồng khám Nội 103',
          roomCode: 'P.103',
        },
      ],
      closedPriorDayQueueCount: 0,
    };
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValue({ data: mockReturnResp });

    renderWithProviders(
      <ReceptionVisitResumeModal open initialVisitCode={visitCode} onClose={vi.fn()} />,
    );

    expect(await screen.findByText(/Đang nghỉ trực/i)).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: /Khám kết luận/i }));
    await user.click(
      screen.getByRole('button', { name: /Xác nhận có mặt & Xếp hàng Khám kết luận/i }),
    );

    // Must block when original doctor is inactive and no replacement selected
    expect(
      await screen.findByText(
        /Bác sĩ phụ trách ban đầu đang nghỉ trực \/ tạm ngưng\. Vui lòng chọn bác sĩ thay thế/i,
      ),
    ).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();

    const select = await screen.findByLabelText(/Chuyển bác sĩ khám kết luận/i);
    await waitFor(() => {
      expect(screen.getByRole('option', { name: /BS\.CKI Trần Thu Hà/i })).toBeInTheDocument();
    });
    await user.selectOptions(select, '22');
    await user.click(
      screen.getByRole('button', { name: /Xác nhận có mặt & Xếp hàng Khám kết luận/i }),
    );

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(
        `/reception/visits/${visitCode}/resume`,
        expect.objectContaining({
          target: 'RETURN_CONSULTATION',
          reassignDoctorId: 22,
        }),
      );
    });
  });

  it('submits Defer / Leave hospital with deferredUntilDate and reason', async () => {
    const user = userEvent.setup();
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: mockContext });
    const mockDeferResp: DeferVisitOrOrdersResponseDto = {
      visitId: 42,
      visitCode,
      visitStatus: 'AWAITING_RESULTS',
      presenceState: 'DEFERRED',
      lastPresentDate: '2026-11-26',
      readyForReturnAt: mockContext.readyForReturnAt,
      deferredUntilDate: '2026-11-27',
      deferredQueueEntryIds: [801],
      updatedDiagnosticOrderIds: [701],
    };
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValue({ data: mockDeferResp });
    const onDeferred = vi.fn();

    renderWithProviders(
      <ReceptionVisitResumeModal
        open
        initialVisitCode={visitCode}
        onClose={vi.fn()}
        onDeferred={onDeferred}
      />,
    );

    await screen.findByText(visitCode);
    await user.click(screen.getByRole('tab', { name: /Tạm hoãn \/ Rời viện/i }));
    await user.type(screen.getByLabelText(/Ngày dự kiến quay lại/i), '2026-11-27');
    await user.type(
      screen.getByLabelText(/Lý do tạm hoãn \/ rời viện/i),
      'Bệnh nhân hẹn sáng mai nhịn ăn lấy mẫu máu',
    );
    await user.click(screen.getByRole('button', { name: /Lưu ghi nhận Tạm hoãn \/ Rời viện/i }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith(`/reception/visits/${visitCode}/defer`, {
        reason: 'Bệnh nhân hẹn sáng mai nhịn ăn lấy mẫu máu',
        deferredUntilDate: '2026-11-27',
        diagnosticOrderIds: null,
        patientLeftHospital: true,
      });
    });
    expect(onDeferred).toHaveBeenCalledWith(mockDeferResp);
  });

  it('renders Patient Portal absent-readiness banner and Doctor/Diagnostic cross-day + overdue badges', () => {
    const absentReadyVisit: PatientVisitListItemDto = {
      visitId: 42,
      visitCode,
      visitStatus: 'AWAITING_RESULTS',
      checkedInAt: '2026-11-24T08:15:00',
      completedAt: null,
      hospitalName: 'Bệnh viện Đa khoa Trung ương MedFlow',
      paymentStatus: 'PAID',
      paymentCode: 'PAY-0042',
      readyForReturnAt: '2026-11-25T16:30:00',
      isPresentToday: false,
      presenceState: 'LEFT',
    };

    renderWithProviders(
      <div>
        <VisitCard visit={absentReadyVisit} />
        <ConsultationPatientHeaderCard
          workspaceState="RETURN_CONSULTATION"
          actionOutcome="VIEWED"
          canEditClinical
          readOnlyReason={null}
          visit={{
            visitId: 42,
            visitCode,
            status: 'RETURN_CONSULTATION',
            statusLabel: 'Khám kết luận',
            visitType: 'WALK_IN',
            visitTypeLabel: 'Khám vãng lai',
            reasonForVisit: 'Đau thượng vị',
            appointmentId: null,
            appointmentCode: null,
            scheduledStartAt: null,
            scheduledEndAt: null,
            checkedInAt: '2026-11-24T08:15:00',
            startedAt: null,
            completedAt: null,
            hospitalId: 1,
            hospitalCode: 'MEDFLOW-01',
            hospitalName: 'Bệnh viện MedFlow',
            departmentId: 1,
            departmentCode: 'NOI',
            departmentName: 'Khoa Nội',
            specialtyId: 1,
            specialtyCode: 'NOI',
            specialtyName: 'Nội tổng quát',
            roomId: 101,
            roomCode: 'P.101',
            roomName: 'Phòng Nội 101',
            roomLocation: 'Tầng 1',
            assignedDoctorId: 11,
            assignedDoctorCode: 'DOC-011',
            assignedDoctorName: 'BS. Nguyễn Minh Khôi',
            assignedDoctorTitle: 'BS',
            serviceId: 1,
            serviceCode: 'KHAM-01',
            serviceName: 'Khám Nội',
            isCrossDayVisit: true,
            isOverdueForReview: true,
          }}
          patient={{
            patientId: 5,
            patientCode: 'PAT-0005',
            fullName: 'Lê Văn Bình',
            dateOfBirth: '1985-04-12',
            birthYear: 1985,
            age: 41,
            gender: 'MALE',
            genderLabel: 'Nam',
            phone: '0901234567',
            email: null,
            address: 'Hà Nội',
            bloodType: 'O+',
            allergiesSummary: null,
          }}
          queue={null}
          openAction={undefined}
          isOpening={false}
          onOpenSession={vi.fn()}
        />
        <DiagnosticWorklistTable
          items={[
            {
              id: 'do-0701',
              orderId: 701,
              orderCode: 'DIA-202611-0701',
              visitId: 42,
              visitCode,
              visitStatus: 'AWAITING_RESULTS',
              patientId: 5,
              patientCode: 'PAT-0005',
              patientName: 'Lê Văn Bình',
              patientGender: 'MALE',
              patientGenderLabel: 'Nam',
              serviceId: 10,
              serviceCode: 'XN-01',
              serviceName: 'Xét nghiệm sinh hóa',
              category: 'LABORATORY',
              categoryLabel: 'Xét nghiệm',
              departmentName: 'Khoa Xét nghiệm',
              specialtyName: 'Xét nghiệm',
              unitPrice: 200000,
              requiresResultVerification: true,
              orderingDoctor: 'BS. Nguyễn Minh Khôi',
              ticketNo: 'C001',
              queueStatus: 'DEFERRED',
              stateVersion: 2,
              room: 'CLS-201',
              priority: 'NORMAL',
              priorityLabel: 'Thường quy',
              status: 'CONFIRMED',
              orderStatus: 'CONFIRMED',
              statusLabel: 'Đã xác nhận',
              requiredForConclusion: true,
              orderedAt: '08:20',
              orderedAtTimestamp: '2026-11-15T08:20:00',
              hasResult: false,
              isResultReady: false,
              isResultFinalized: false,
              resultStatus: 'PENDING',
              isAdditionalOrder: false,
              orderSequenceNo: 1,
              canConfirmOrder: false,
              canReceivePatient: false,
              canUpdateExecutionStatus: false,
              canEnterResult: false,
              canViewResult: false,
              allowedActions: [],
              deferredUntilDate: '2026-11-28',
              isCrossDayOrder: true,
              isExpiredForReview: true,
            } satisfies DiagnosticWorklistItemDto,
          ]}
          canConfirmOrders
          canManageQueue
          canWriteResults
          onSelectOrder={vi.fn()}
          onTriggerAction={vi.fn()}
        />
      </div>,
    );

    expect(
      screen.getByText(
        'Đủ điều kiện quay lại khám kết luận — vui lòng đến quầy Tiếp đón xác nhận có mặt',
      ),
    ).toBeInTheDocument();
    expect(screen.getAllByText('Lượt khám nhiều ngày').length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText('Quá hạn — cần xem xét').length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('Hẹn ngày 2026-11-28')).toBeInTheDocument();
    expect(screen.getByText('Tạm hoãn qua ngày')).toBeInTheDocument();
  });
});
