import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { CheckInResultContent, CheckInResultModal } from '../components/checkin-result-modal';
import type { CheckInSuccessResult } from '../types/checkin.types';
import { ReceptionDashboardPage } from '../pages/reception-dashboard-page';

const mockResult: CheckInSuccessResult = {
  visitId: 'VIS-202610-0018',
  ticketNo: 'A018',
  patientName: 'Đỗ Gia Huy',
  patientCode: 'BN-2024-0018',
  roomName: 'P.102',
  departmentName: 'Khoa Nội tổng quát',
  doctorName: 'BS. CKII Trần Khánh',
  checkedInAt: '08:45',
  priority: 'ELDERLY',
  queueNumber: 4,
  waitingCountBefore: 3,
  estimatedWaitMinutes: 15,
  nextStepInstructions: [
    'Di chuyển đến trước P.102 (Khoa Nội tổng quát - Tầng 1).',
    'Theo dõi màn hình điện tử và lắng nghe loa gọi số thứ tự tiếp nhận.',
    'Chuẩn bị sẵn thẻ CCCD/BHYT bản gốc trước khi vào phòng khám.',
  ],
};

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('View Check-in Result Components (Issue #73)', () => {
  describe('CheckInResultContent', () => {
    it('renders ticket details, room routing, and visit code', () => {
      const handleClose = vi.fn();
      render(<CheckInResultContent result={mockResult} onClose={handleClose} />);

      expect(screen.getByTestId('checkin-success-view')).toBeInTheDocument();
      expect(screen.getByText('A018')).toBeInTheDocument();
      expect(screen.getByText('VIS-202610-0018')).toBeInTheDocument();
      expect(screen.getByText('Đỗ Gia Huy')).toBeInTheDocument();
      expect(screen.getByText(/P\.102 • Khoa Nội tổng quát/i)).toBeInTheDocument();
      expect(screen.getByText('BS. CKII Trần Khánh')).toBeInTheDocument();
      expect(screen.getByText('08:45')).toBeInTheDocument();
    });

    it('renders priority badge correctly', () => {
      render(<CheckInResultContent result={mockResult} onClose={vi.fn()} />);
      expect(screen.getByText('Ưu tiên người cao tuổi')).toBeInTheDocument();
    });

    it('renders queue wait status and patient next step instructions', () => {
      render(<CheckInResultContent result={mockResult} onClose={vi.fn()} />);

      expect(screen.getByText('3 người')).toBeInTheDocument();
      expect(screen.getByText('~15 phút')).toBeInTheDocument();
      expect(screen.getByText(/Hướng dẫn tiếp theo cho người bệnh/i)).toBeInTheDocument();
      expect(
        screen.getByText(/Di chuyển đến trước P\.102 \(Khoa Nội tổng quát - Tầng 1\)\./i),
      ).toBeInTheDocument();
    });

    it('triggers print and close handlers', () => {
      const handleClose = vi.fn();
      const handlePrint = vi.fn();
      render(
        <CheckInResultContent result={mockResult} onClose={handleClose} onPrint={handlePrint} />,
      );

      const printBtn = screen.getByRole('button', { name: /In phiếu tiếp nhận/i });
      fireEvent.click(printBtn);
      expect(handlePrint).toHaveBeenCalledOnce();

      const closeBtn = screen.getByRole('button', { name: /Hoàn tất & Đóng/i });
      fireEvent.click(closeBtn);
      expect(handleClose).toHaveBeenCalledOnce();
    });
  });

  describe('CheckInResultModal', () => {
    it('does not render dialog when isOpen is false', () => {
      render(<CheckInResultModal isOpen={false} onClose={vi.fn()} result={mockResult} />);
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('renders modal dialog with accessible title and close button', () => {
      const handleClose = vi.fn();
      render(<CheckInResultModal isOpen={true} onClose={handleClose} result={mockResult} />);

      expect(screen.getByRole('dialog')).toBeInTheDocument();
      expect(
        screen.getByRole('heading', { name: /Kết Quả Tiếp Nhận & Phiếu Khám/i }),
      ).toBeInTheDocument();

      const closeBtn = screen.getByRole('button', { name: /Đóng kết quả check-in/i });
      fireEvent.click(closeBtn);
      expect(handleClose).toHaveBeenCalledOnce();
    });
  });

  describe('ReceptionDashboardPage Integration', () => {
    afterEach(() => {
      vi.restoreAllMocks();
    });

    it('loads the checked-in result from the API when clicking "Xem kết quả"', async () => {
      useAuthStore.setState({
        user: {
          id: 1,
          fullName: 'Lễ Tân Test',
          email: 'letan@medflow.vn',
          roles: ['RECEPTION'],
          patientCode: null,
          staffCode: 'NV-001',
        },
        accessToken: 'mock-token',
        refreshToken: 'mock-refresh-token',
        isAuthenticated: true,
      });

      vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
        if (url === '/reception/patients/search') {
          return {
            data: {
              items: [
                {
                  id: 1,
                  patientCode: 'BN-2024-0018',
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
                    status: 'CHECKED_IN',
                  },
                },
              ],
              totalCount: 1,
              page: 1,
              pageSize: 50,
              counts: { all: 1, hasAppointment: 1, checkedIn: 1, noAppointment: 0 },
            },
          } as never;
        }
        if (typeof url === 'string' && url.endsWith('/check-in-result')) {
          return {
            data: {
              visitId: 18,
              visitCode: 'VIS-202610-0018',
              ticketNo: 'A018',
              queueNumber: 4,
              priority: 'ELDERLY',
              patientCode: 'BN-2024-0018',
              patientName: 'Đỗ Gia Huy',
              departmentName: 'Khoa Nội tổng quát',
              roomName: 'P.102',
              doctorName: 'BS. CKII Trần Khánh',
              waitingCountBefore: 3,
              estimatedWaitMinutes: 15,
              checkedInAt: '08:45',
              nextStepInstructions: [],
            },
          } as never;
        }
        return { data: {} } as never;
      });

      renderWithProviders(<ReceptionDashboardPage />);

      fireEvent.click(await screen.findByRole('button', { name: /Xem kết quả/i }));

      expect(
        await screen.findByRole('heading', { name: /Kết Quả Tiếp Nhận & Phiếu Khám/i }),
      ).toBeInTheDocument();
      expect(screen.getByText('A018')).toBeInTheDocument();
      expect(screen.getByText('VIS-202610-0018')).toBeInTheDocument();
    });
  });
});
