import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { CheckInAppointmentSummary } from '../components/checkin-appointment-summary';
import { CheckInModal } from '../components/checkin-modal';
import { CheckInVitalsSection } from '../components/checkin-vitals-section';
import { SCHEDULED_CHECKIN_APPOINTMENTS_MOCK } from '../data/checkin-mock';
import { ReceptionDashboardPage } from '../pages/reception-dashboard-page';

const mockAppointment = SCHEDULED_CHECKIN_APPOINTMENTS_MOCK[0];

describe('Check-in Scheduled Patient Components (Issue #71)', () => {
  describe('CheckInAppointmentSummary', () => {
    it('renders appointment key facts and patient details', () => {
      render(<CheckInAppointmentSummary appointment={mockAppointment} />);

      expect(screen.getByText('APT-2024-0012')).toBeInTheDocument();
      expect(screen.getByText('Đỗ Gia Huy')).toBeInTheDocument();
      expect(screen.getByText('BS. CKII Trần Khánh')).toBeInTheDocument();
      expect(screen.getByText('P.102')).toBeInTheDocument();
      expect(screen.getByText('DN4010123456789')).toBeInTheDocument();
    });
  });

  describe('CheckInVitalsSection', () => {
    it('renders inputs and priority selection', () => {
      const handleChange = vi.fn();
      render(
        <CheckInVitalsSection
          formData={{
            reasonForVisit: '',
            isIdentityVerified: false,
            isInsuranceVerified: false,
            priority: 'NORMAL',
          }}
          onChange={handleChange}
        />,
      );

      expect(screen.getByLabelText(/Lý do đến khám hôm nay/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/Huyết áp/i)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Người cao tuổi/i })).toBeInTheDocument();

      const elderlyBtn = screen.getByRole('button', { name: /Người cao tuổi/i });
      fireEvent.click(elderlyBtn);
      expect(handleChange).toHaveBeenCalledWith({ priority: 'ELDERLY' });
    });
  });

  describe('CheckInModal', () => {
    it('shows validation errors if required fields are missing', async () => {
      render(
        <CheckInModal
          isOpen={true}
          onClose={vi.fn()}
          appointment={mockAppointment}
          onSubmit={vi.fn()}
        />,
      );

      const submitBtn = screen.getByRole('button', { name: /Xác nhận Check-in & Cấp STT/i });
      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(screen.getByText(/Lý do khám sơ bộ phải có ít nhất 3 ký tự/i)).toBeInTheDocument();
        expect(
          screen.getByText(/Bắt buộc xác nhận đã đối chiếu giấy tờ tùy thân của bệnh nhân/i),
        ).toBeInTheDocument();
      });
    });

    it('submits successfully when form is valid and displays ticket result', async () => {
      const handleSuccess = vi.fn();
      render(
        <CheckInModal
          isOpen={true}
          onClose={vi.fn()}
          appointment={mockAppointment}
          onSubmit={async () => ({
            visitId: '12',
            visitCode: 'VIS-TEST-0012',
            ticketNo: 'A018',
            patientName: mockAppointment.patientName,
            patientCode: mockAppointment.patientCode,
            roomName: mockAppointment.roomName,
            departmentName: mockAppointment.department,
            doctorName: mockAppointment.doctorName,
            checkedInAt: '09:20',
            priority: 'NORMAL',
            queueNumber: 18,
            waitingCountBefore: 3,
            estimatedWaitMinutes: 15,
            nextStepInstructions: [],
          })}
          onSuccess={handleSuccess}
        />,
      );

      // Fill reason
      const reasonInput = screen.getByLabelText(/Lý do đến khám hôm nay/i);
      fireEvent.change(reasonInput, {
        target: { value: 'Tái khám huyết áp và xét nghiệm định kỳ' },
      });

      // Check identity verification
      const verifyCheckbox = screen.getByRole('checkbox', {
        name: /Đã đối chiếu thông tin CCCD \/ VNeID trùng khớp/i,
      });
      fireEvent.click(verifyCheckbox);

      // Submit
      const submitBtn = screen.getByRole('button', { name: /Xác nhận Check-in & Cấp STT/i });
      fireEvent.click(submitBtn);

      await waitFor(
        () => {
          expect(screen.getByTestId('checkin-success-view')).toBeInTheDocument();
          expect(screen.getByText(/Check-in Thành Công/i)).toBeInTheDocument();
          expect(screen.getByText('A018')).toBeInTheDocument();
        },
        { timeout: 2000 },
      );

      expect(handleSuccess).toHaveBeenCalledWith(
        expect.objectContaining({
          ticketNo: 'A018',
          patientName: mockAppointment.patientName,
        }),
      );
    });
  });

  describe('ReceptionDashboardPage Integration', () => {
    afterEach(() => {
      vi.restoreAllMocks();
    });

    it('opens CheckInModal when clicking Check-in button in appointment list', async () => {
      vi.spyOn(apiClient, 'get').mockResolvedValue({
        data: {
          items: [
            {
              id: 1,
              patientCode: 'BN-TEST-01',
              fullName: 'Đỗ Gia Huy',
              gender: 'MALE',
              dateOfBirth: '1988-05-14',
              identityCardMasked: '0010***345',
              phone: '0912345678',
              address: 'Hà Nội',
              totalPastVisits: 2,
              todayAppointment: {
                id: 12,
                time: '09:15',
                doctorName: 'BS. CKII Trần Khánh',
                department: 'Nội tổng quát',
                roomName: 'P.102',
                status: 'BOOKED',
              },
            },
          ],
          totalCount: 1,
          page: 1,
          pageSize: 50,
          counts: { all: 1, hasAppointment: 1, checkedIn: 0, noAppointment: 0 },
        },
      } as never);

      useAuthStore.setState({
        user: {
          id: 5,
          email: 'letan@medflow.test',
          fullName: 'Lê Tân',
          roles: ['RECEPTION'],
          patientCode: null,
        },
        accessToken: 'tok',
        refreshToken: 'rtok',
        isAuthenticated: true,
      });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      render(
        <QueryClientProvider client={qc}>
          <MemoryRouter>
            <ReceptionDashboardPage />
          </MemoryRouter>
        </QueryClientProvider>,
      );

      const checkInBtn = await screen.findByRole('button', { name: 'Check-in' });
      expect(checkInBtn).toBeInTheDocument();

      fireEvent.click(checkInBtn);

      expect(screen.getByRole('dialog')).toBeInTheDocument();
      expect(screen.getByText(/Xác Nhận Check-in Lịch Hẹn Khám/i)).toBeInTheDocument();
    });
  });
});
